using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO.Ports;
using System.Threading;
using System.IO;

namespace Mobot
{   
    public partial class Form1 : Form
    {
        private static int aktualnyNrStanowiska = 0;
        private static int poprzedniNrStanowiska = 0;
        private static bool odebranoPoprawnie;
        private static int skadPobranoPaczke = 0;
        private static int krok = 0;
        private static bool podniesionaPaczka = false;
        private static int ilePaczek = 0;
        private static float sonarPrawy, sonarLewy, pradLewy, pradPrawy, bateria;
        private static int koniecRamki; //bajt konczacy wysylanie ramki, 8 bit - stan czujnika srodkowego, 7 bit - stan elektromagnesu, pozostale bity powinny byc sekwencja "101010"
        private static float bateria_przeliczone;
        private static byte stanLedICzuj; //stan czujnikow i diod led
        private static bool _continue;
        private static SerialPort _serialPort;
        private delegate void Update();
        private Update myDelegate1;
        private Thread readThread, sendThread; //watki do odbioru i przesylu danych od/do robota
        private DateTime czasstart, czaskoniec; //zmienne do obliczania czasu przejazdu w trybie testowym
        private DateTime czasStartWysylania, czasKoniecWysylania; //zmienne do ograniczenia czasu pracy wątku wysyłającego polecenie do robota
        private TimeSpan czasPrzejazduTestowego; //czas trwania przejazdu w trybie testowym
        private static List<float> pradyLewe = new List<float>(); //W trybie testowym przechowuje serie pomiarow
        private static List<float> pradyPrawe = new List<float>(); //W trybie testowym przechowuje serie pomiarow
        private StreamWriter pomiary; //Sluzy do zapisania pomiarow w pliku tekstowym
 
        public Form1()
        {
            InitializeComponent();
            myDelegate1 = new Update(UpdateUI);
            domainUpDown1.SelectedIndex = 100;
            readThread = new Thread(Read); 
            sendThread = new Thread(new ParameterizedThreadStart(Send));         
            timer1.Start();
            
            //ustawienie braku paczek na stanowiskach
            pictureBoxBOX1.Tag = "brak";
            pictureBoxBOX2.Tag = "brak";
            pictureBoxBOX3.Tag = "brak";
            pictureBoxBOX4.Tag = "brak";
            pictureBoxBOX5.Tag = "brak";
            pictureBoxBOX6.Tag = "brak";

            //wypozycjonowanie kontrolek na formatce
            pictureBoxBOX1.Parent = pictureBox3;
            pictureBoxBOX1.Location = new Point(99, 2);
            pictureBoxBOX2.Parent = pictureBox3;
            pictureBoxBOX2.Location = new Point(100, 274);
            pictureBoxBOX3.Parent = pictureBox3;
            pictureBoxBOX3.Location = new Point(228, 2);
            pictureBoxBOX4.Parent = pictureBox3;
            pictureBoxBOX4.Location = new Point(228, 274);
            pictureBoxBOX5.Parent = pictureBox3;
            pictureBoxBOX5.Location = new Point(356, 2);
            pictureBoxBOX6.Parent = pictureBox3;
            pictureBoxBOX6.Location = new Point(356, 274);
            pictureBoxLedPL.Parent = pictureBox1;
            pictureBoxLedPP.Parent = pictureBox1;
            pictureBoxLedTL.Parent = pictureBox1;
            pictureBoxLedTP.Parent = pictureBox1;
            pictureBoxCzujPL.Parent = pictureBox1;
            pictureBoxCzujPP.Parent = pictureBox1;
            pictureBoxCzujTL.Parent = pictureBox1;
            pictureBoxCzujTP.Parent = pictureBox1;
            pictureBoxCzujS.Parent = pictureBox1;

            //widocznosc nieaktywna (poszczególne pictureBoxy stają się widoczne gdy zostanie odebrana ramka z informacją które czujniki lub diody są aktywne)
            pictureBoxLedPL.Visible = false;
            pictureBoxLedPP.Visible = false;
            pictureBoxLedTL.Visible = false;
            pictureBoxLedTP.Visible = false;
            pictureBoxCzujPL.Visible = false;
            pictureBoxCzujPP.Visible = false;
            pictureBoxCzujTL.Visible = false;
            pictureBoxCzujTP.Visible = false;
            pictureBoxCzujS.Visible = false;
            pictureBoxMagnes.Visible = false;

            label6.Text = "Średni prąd na lewym mostku: -";
            label7.Text = "Średni prąd na prawym mostku: -";
            label8.Text = "Czas przejazdu: -";
            label9.Text = "Średnia prędkość: -";    
            _continue = true;
        }

        //Aktualizacja interfejsu po odebraniu ramki danych
        private void UpdateUI()
        {            
            if (this.InvokeRequired)
            {
                Invoke(myDelegate1);
            }
            else
            { 
                //przeliczenie wartości pomiarowych na rzeczywiste (patrz: --->ADC ATMega, ---> schemat robota)
                bateria_przeliczone = (float)((((bateria) * 2.56) / 1024) / 0.248);             
                double pradLewy_przeliczone = (((pradLewy*2.56)/1024)*1000);
                double pradPrawy_przeliczone = (((pradPrawy*2.56)/1024)*1000);

                //wpisanie wartości na formatke
                labelMostekLewy.Text = pradLewy_przeliczone.ToString() + " [mA]";
                labelMostekPrawy.Text = pradPrawy_przeliczone.ToString() + " [mA]";
                labelSonarLewy.Text = sonarLewy.ToString() + " [cm]";
                labelSonarPrawy.Text = sonarPrawy.ToString() + " [cm]";
                
                //sprawdzenie ledow
                if (Convert.ToBoolean(stanLedICzuj & (1 << 7))) pictureBoxLedPL.Visible = true;
                else pictureBoxLedPL.Visible = false;
                if (Convert.ToBoolean(stanLedICzuj & (1 << 6))) pictureBoxLedPP.Visible = true;
                else pictureBoxLedPP.Visible = false;
                if (Convert.ToBoolean(stanLedICzuj & (1 << 5))) pictureBoxLedTL.Visible = true;
                else pictureBoxLedTL.Visible = false;
                if (Convert.ToBoolean(stanLedICzuj & (1 << 4))) pictureBoxLedTP.Visible = true;
                else pictureBoxLedTP.Visible = false;
                //sprawdzenie czujnikow
                if (Convert.ToBoolean(stanLedICzuj & (1 << 3))) pictureBoxCzujPL.Visible = true;
                else pictureBoxCzujPL.Visible = false;
                if (Convert.ToBoolean(stanLedICzuj & (1 << 2))) pictureBoxCzujPP.Visible = true;
                else pictureBoxCzujPP.Visible = false;
                if (Convert.ToBoolean(stanLedICzuj & (1 << 1))) pictureBoxCzujTL.Visible = true;
                else pictureBoxCzujTL.Visible = false;
                if (Convert.ToBoolean(stanLedICzuj & 1)) pictureBoxCzujTP.Visible = true;
                else pictureBoxCzujTP.Visible = false;

                //środkowy czujnik
                if (Convert.ToBoolean(koniecRamki & (1 << 7))) pictureBoxCzujS.Visible = true;
                else pictureBoxCzujS.Visible = false;

                //magnes
                if (Convert.ToBoolean(koniecRamki & (1 << 6))) pictureBoxMagnes.Visible = true;
                else pictureBoxMagnes.Visible = false;

                //Tryb testowy
                if(toolStripStatusLabel4.Text.Equals("Testowy"))
                {
                    //pomiar czasu
                    czaskoniec = DateTime.Now;
                    czasPrzejazduTestowego = czaskoniec - czasstart;

                    //zapis do tablicy
                    pradyLewe.Add((float)pradLewy_przeliczone);
                    pradyPrawe.Add((float)pradPrawy_przeliczone);
                    //zapis pomiaru do pliku
                    if(pomiary != null) pomiary.WriteLine(pradLewy_przeliczone.ToString() + ";" + pradPrawy_przeliczone.ToString()+";" + czasPrzejazduTestowego.TotalMilliseconds.ToString());


                    if (krok == 1) //pomiar ukończono
                    {
                        float sredniPradLewy = 0;
                        float sredniPradPrawy = 0;

                        for(int i = 0; i<pradyLewe.Count; ++i)
                        {
                            sredniPradLewy += pradyLewe[i];
                            sredniPradPrawy += pradyPrawe[i];
                        }
                        sredniPradPrawy = sredniPradPrawy / pradyPrawe.Count;
                        sredniPradLewy = sredniPradLewy / pradyPrawe.Count;

                        label6.Text = "Sredni prąd na lewym mostku: " + Math.Round(sredniPradLewy, 2) + " [mA]";
                        label7.Text = "Sredni prąd na prawym mostku: " + Math.Round(sredniPradPrawy, 2) + " [mA]";                       
                        label8.Text = "Czas przejazdu: " + Math.Round(czasPrzejazduTestowego.TotalSeconds,2).ToString() + " [s]";
                        label9.Text = "Średnia prędkość: " + Math.Round(0.5/czasPrzejazduTestowego.TotalSeconds,2).ToString() + " [m/s]";

                        buttonStartPomiar.Enabled = true;
                        domainUpDown1.Enabled = true;
                        pradyLewe.Clear();
                        pradyPrawe.Clear();
                        if (pomiary != null) pomiary.Close();
                        pomiary = null;
                    }
                } 

                //W trybie automatycznym wysyla kolejne polecenie jesli takie istnieje, aktualizuje podglad sytuacji na makiecie, usuwa wykonane zadanie z listy
                //sprawdzenie czy dojechal do celu
                if (listView1.Items.Count > 0 && toolStripStatusLabel4.Text.Equals("Automatyczny")) 
                {
                    int skad = Convert.ToInt16(listView1.TopItem.Text);
                    int dokad = Convert.ToInt16(listView1.TopItem.SubItems[1].Text);
                    PictureBox picSkad = this.Controls.Find("pictureBoxBOX" + skad.ToString(), true).FirstOrDefault() as PictureBox;
                    PictureBox picDokad = this.Controls.Find("pictureBoxBOX" + dokad.ToString(), true).FirstOrDefault() as PictureBox;
                    int skrzyzowanieStartowe = 0;
                    int skrzyzowanieKoncowe = 0;
                    int odlegloscSkrzyzowan;
                    if (skad == 1 || skad == 2) skrzyzowanieStartowe = 1;
                    else if (skad == 3 || skad == 4) skrzyzowanieStartowe = 2;
                    else if (skad == 5 || skad == 6) skrzyzowanieStartowe = 3;
                    if (dokad == 1 || dokad == 2) skrzyzowanieKoncowe = 1;
                    else if (dokad == 3 || dokad == 4) skrzyzowanieKoncowe = 2;
                    else if (dokad == 5 || dokad == 6) skrzyzowanieKoncowe = 3;
                    odlegloscSkrzyzowan = Math.Abs(skrzyzowanieKoncowe - skrzyzowanieStartowe);

                    if (aktualnyNrStanowiska != poprzedniNrStanowiska && aktualnyNrStanowiska == Convert.ToInt16(listView1.TopItem.SubItems[1].Text))
                    {
                        //Dojechal z paczka do celu
                        pictureBoxSkrzyzowanie1.Visible = false;
                        pictureBoxSkrzyzowanie2.Visible = false;
                        pictureBoxSkrzyzowanie3.Visible = false;
                        picDokad.Tag = "paczka";
                        picDokad.Image = Mobot.Properties.Resources.paczka;

                        listView1.Items.RemoveAt(0);
                        if (listView1.Items.Count > 0)
                        {
                            //Jedz po kolejną paczke
                            sendThread = new Thread(new ParameterizedThreadStart(Send));
                            sendThread.Start(Convert.ToInt16(listView1.TopItem.Text));
                        }
                        else
                        {
                            //Lista polecen wykonana
                            sendThread = new Thread(new ParameterizedThreadStart(Send));
                            sendThread.Start(13);
                            pictureBox3.Enabled = true;
                            buttonWykonaj.Enabled = true;                      
                            groupBox4.Enabled = true;
                        }   
                    }
                    else if (aktualnyNrStanowiska != poprzedniNrStanowiska && aktualnyNrStanowiska == Convert.ToInt16(listView1.TopItem.Text))
                    {
                        //Dojechal po paczke
                        //Zawiez paczke do celu
                        picSkad.Tag = "paczkaPobierana";
                        picSkad.Image = Mobot.Properties.Resources.paczka;
                        sendThread = new Thread(new ParameterizedThreadStart(Send));
                        sendThread.Start(Convert.ToInt16(listView1.TopItem.SubItems[1].Text));
                    }
                    else if (aktualnyNrStanowiska == poprzedniNrStanowiska && pictureBoxMagnes.Visible == true)
                    {
                        //Śledzenie paczki
                        if (odlegloscSkrzyzowan == 0)
                        {
                            if (krok == 1)
                            {
                                picSkad.Tag = "brak";
                                picSkad.Image = null;
                                PictureBox pic1 = this.Controls.Find("pictureBoxSkrzyzowanie" + skrzyzowanieStartowe.ToString(), true).FirstOrDefault() as PictureBox;
                                pic1.Visible = true;
                            }
                        }
                        else if (odlegloscSkrzyzowan == 1)
                        {
                            if (krok == 1)
                            {
                                picSkad.Tag = "brak";
                                picSkad.Image = null;
                                PictureBox pic1 = this.Controls.Find("pictureBoxSkrzyzowanie" + skrzyzowanieStartowe.ToString(), true).FirstOrDefault() as PictureBox;
                                pic1.Visible = true;
                            }
                            if (krok == 2)
                            {
                                PictureBox pic1 = this.Controls.Find("pictureBoxSkrzyzowanie" + skrzyzowanieKoncowe.ToString(), true).FirstOrDefault() as PictureBox;
                                if(!pictureBoxSkrzyzowanie1.Name.Equals(pic1.Name)) pictureBoxSkrzyzowanie1.Visible = false;
                                if (!pictureBoxSkrzyzowanie2.Name.Equals(pic1.Name)) pictureBoxSkrzyzowanie2.Visible = false;
                                if (!pictureBoxSkrzyzowanie3.Name.Equals(pic1.Name)) pictureBoxSkrzyzowanie3.Visible = false; 
                                pic1.Visible = true;
                            }
                        }
                        else if (odlegloscSkrzyzowan == 2)
                        {
                            if (krok == 1)
                            {
                                picSkad.Tag = "brak";
                                picSkad.Image = null;
                                PictureBox pic1 = this.Controls.Find("pictureBoxSkrzyzowanie" + skrzyzowanieStartowe.ToString(), true).FirstOrDefault() as PictureBox;
                                pic1.Visible = true;
                            }
                            if (krok == 2)
                            {
                                pictureBoxSkrzyzowanie1.Visible = false;
                                pictureBoxSkrzyzowanie2.Visible = true;
                                pictureBoxSkrzyzowanie3.Visible = false;
                            }
                            if (krok == 3)
                            {
                                pictureBoxSkrzyzowanie2.Visible = false;
                                PictureBox pic1 = this.Controls.Find("pictureBoxSkrzyzowanie" + skrzyzowanieKoncowe.ToString(), true).FirstOrDefault() as PictureBox;
                                pic1.Visible = true;
                            }
                        }
                    }
                }
                poprzedniNrStanowiska = aktualnyNrStanowiska;
            }
        }

        //Funkcja wywolywana przez watek sendThread, jest wykonywany przez 1 sek.
        public void Send(object parameter)
        {
            czasStartWysylania = DateTime.Now;
            switch (Convert.ToInt16(parameter))
            {
                case 1:
                    {
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 0x01;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);

                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 2:
                    {
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 0x02;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 3:
                    {                      
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 0x03;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 4:
                    {                       
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 0x04;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 5:
                    {                       
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 0x05;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 6:
                    {                        
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 0x06;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 7: //wlacz magnes
                    {
                        
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 0xBB;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 8: //wylacz magnes
                    {
                        
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 0xAA;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 9: // Tryb ręczny
                    {
                        
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 107;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 10: // Tryb automatyczny
                    {
                        
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 108;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 11: // Tryb testowy
                    {
                        
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 109;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 12: // Rozpocznij test
                    {
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = (byte)Convert.ToInt16(domainUpDown1.Text);
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
                case 13: // Powrot do Startu
                    {
                        while (odebranoPoprawnie == false)
                        {
                            czasKoniecWysylania = DateTime.Now;
                            byte[] bufor = new byte[1];
                            bufor[0] = 0x11;
                            _serialPort.Write(bufor, 0, 1);
                            Thread.Sleep(50);
                            TimeSpan czasTrwania = czasKoniecWysylania - czasStartWysylania;
                            if (czasTrwania.TotalMilliseconds >= 1000)
                            {
                                Thread.CurrentThread.Abort();
                                break;
                            }
                        }
                        odebranoPoprawnie = false;
                        Thread.CurrentThread.Abort();
                        break;
                    }
            }
        }
        //Funkcja wywolywana przez watek readThread (odbieranie danych z robota)
        public void Read()
        {
            int nrBajtuDanych = 0;
            bool blok = true;
            byte bajt = 0;
            byte poprzedniBajt = 0;
            while (_continue)
            {
                try
                {
                    bajt = (byte)_serialPort.ReadByte();
                    if (blok == false)
                    {
                        if (nrBajtuDanych == 0)
                        {
                            sonarLewy = bajt;
                            ++nrBajtuDanych;
                        }
                        else if (nrBajtuDanych == 1)
                        {
                            sonarPrawy = bajt;
                            ++nrBajtuDanych;
                        }
                        else if (nrBajtuDanych == 2)
                        {
                            pradLewy = (bajt);
                            ++nrBajtuDanych;
                        }
                        else if (nrBajtuDanych == 3)
                        {
                            pradPrawy = (bajt);
                            ++nrBajtuDanych;
                        }
                        else if (nrBajtuDanych == 4)
                        {
                            stanLedICzuj = bajt;
                            ++nrBajtuDanych;
                        }
                        else if (nrBajtuDanych == 5)
                        {
                            bateria = (bajt << 2);
                            ++nrBajtuDanych;
                        }
                        else if (nrBajtuDanych == 6)
                        {
                            aktualnyNrStanowiska = bajt;
                            ++nrBajtuDanych;
                        }
                        else if (nrBajtuDanych == 7)
                        {
                            krok = bajt;
                            ++nrBajtuDanych;
                        }
                        else if (nrBajtuDanych == 8)
                        {                           
                            byte temp = (byte)(bajt << 2);
                            koniecRamki = bajt;     //bajt konczacy wysylanie ramki, 8 bit - stan czujnika srodkowego, 7 bit - stan elektromagnesu                      
                            if (temp == 0xA8)
                            {
                                UpdateUI(); //uaktualnienie interfejsu gdy ramka poprawna
                            }
                            nrBajtuDanych = 0;                            
                            bajt = 0;
                            poprzedniBajt = 0;
                            blok = true;
                        }
                    }
                    if (bajt == 50 && poprzedniBajt == 100 && blok == true)
                    {
                        //poprawny naglowek ramki, nastąpi odebranie ramki z danymi
                        blok = false;
                    }
                    if (bajt == 0xBB && poprzedniBajt == 0xBB && blok == true)
                    {
                        odebranoPoprawnie = true; //robot odebrał poprawnie polecenie
                    }
                    poprzedniBajt = bajt;
                }
                catch (TimeoutException) { }
                if(toolStripStatusLabel2.Text == "Rozłączony")
                {
                    //Zatrzymanie pracy wątku gdy zostanie wciśnięty przycisk "Rozłącz"
                    _serialPort.Close();
                    _serialPort.Dispose();
                    Thread.CurrentThread.Abort();
                    
                }
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //Wypełnienie ComboBoxa dostępnymi portami COM
            comboBox1.Items.AddRange(SerialPort.GetPortNames());
            comboBox1.SelectedIndex = 0;                    
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {           
            timer1.Stop();
            if (buttonPolaczRozlacz.Text.Equals("Rozłącz"))
            {
                readThread.Abort();
                _serialPort.Close();  
            }         
        }

        private void buttonPolaczRozlacz_Click(object sender, EventArgs e)
        {
            if (buttonPolaczRozlacz.Text.Equals("Połącz"))
            {
                if (comboBox1.Items.Count > 0)
                {
                    _serialPort = new SerialPort(comboBox1.Items[0].ToString(), 57600, Parity.None, 8, StopBits.One);
                    // Set the read/write timeouts
                    _serialPort.ReadTimeout = 500;
                    _serialPort.WriteTimeout = 500;
                    _serialPort.Open();
                    readThread = new Thread(Read);
                    readThread.Start();
                    buttonPolaczRozlacz.Text = "Rozłącz";
                    toolStripStatusLabel2.Text = "Połączony";
                    groupBox4.Enabled = true;
                    buttonAutomatyczny.Enabled = true;
                    buttonReczny.Enabled = true;
                    buttonTestowy.Enabled = true;
                }
            }
            else
            {
                buttonPolaczRozlacz.Text = "Połącz";
                toolStripStatusLabel2.Text = "Rozłączony";
                groupBox1.Enabled = false;
                groupBox2.Enabled = false;
                groupBox4.Enabled = false;
                groupBox5.Enabled = false;

                pictureBoxSkrzyzowanie1.Visible = false;
                pictureBoxSkrzyzowanie2.Visible = false;
                pictureBoxSkrzyzowanie3.Visible = false;

                pictureBoxBOX1.Tag = "brak";
                pictureBoxBOX1.Image = null;
                pictureBoxBOX2.Tag = "brak";
                pictureBoxBOX2.Image = null;
                pictureBoxBOX3.Tag = "brak";
                pictureBoxBOX3.Image = null;
                pictureBoxBOX4.Tag = "brak";
                pictureBoxBOX4.Image = null;
                pictureBoxBOX5.Tag = "brak";
                pictureBoxBOX5.Image = null;
                pictureBoxBOX6.Tag = "brak";
                pictureBoxBOX6.Image = null;

                toolStripStatusLabel4.Text = "-";
            }           
        }

        //Obsługa przycisków, wysyłających polecenie w trybie ręcznym
        private void button2_Click(object sender, EventArgs e)
        {
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(1);
        }
        private void button3_Click(object sender, EventArgs e)
        {
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(2);
        }
        private void button4_Click(object sender, EventArgs e)
        {
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(3);
        }
        private void button5_Click(object sender, EventArgs e)
        {
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(4);
        }
        private void button6_Click(object sender, EventArgs e)
        {
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(5);
        }
        private void button7_Click(object sender, EventArgs e)
        {
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(6);
        }
        private void buttonWlaczMagnes_Click(object sender, EventArgs e)
        {
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(7);
        }
        private void buttonWylaczMagnes_Click(object sender, EventArgs e)
        {
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(8);
        }
        //odświeżenie stanu baterii (co 1 sek.)
        private void timer1_Tick(object sender, EventArgs e)
        {
            toolStripBateria.Text = Math.Round(bateria_przeliczone, 2).ToString() + " V  ";
        }

        //checkboxy do wstępnego położenia "paczek"
        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                pictureBoxBOX1.Image = Mobot.Properties.Resources.paczka;
                pictureBoxBOX1.Tag = "paczka";
                ++ilePaczek;
                if (ilePaczek == 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(),true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Checked) checkBox.Enabled = false;
                    }
                }
            }
            else
            {
                pictureBoxBOX1.Image = null;
                pictureBoxBOX1.Tag = "brak";
                --ilePaczek;
                if (ilePaczek < 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Enabled) checkBox.Enabled = true;
                    }
                }
            }
        }
        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox2.Checked)
            {
                pictureBoxBOX2.Image = Mobot.Properties.Resources.paczka;
                pictureBoxBOX2.Tag = "paczka";
                ++ilePaczek;
                if (ilePaczek == 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Checked) checkBox.Enabled = false;
                    }
                }
            }
            else
            {
                pictureBoxBOX2.Image = null;
                pictureBoxBOX2.Tag = "brak";
                --ilePaczek;
                if (ilePaczek < 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Enabled) checkBox.Enabled = true;
                    }
                }
            }
        }
        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox3.Checked)
            {
                pictureBoxBOX3.Image = Mobot.Properties.Resources.paczka;
                pictureBoxBOX3.Tag = "paczka";
                ++ilePaczek;
                if (ilePaczek == 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Checked) checkBox.Enabled = false;
                    }
                }
            }
            else
            {
                pictureBoxBOX3.Image = null;
                pictureBoxBOX3.Tag = "brak";
                --ilePaczek;
                if (ilePaczek < 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Enabled) checkBox.Enabled = true;
                    }
                }
            }
        }
        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox4.Checked)
            {
                pictureBoxBOX4.Image = Mobot.Properties.Resources.paczka;
                pictureBoxBOX4.Tag = "paczka";
                ++ilePaczek;
                if (ilePaczek == 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Checked) checkBox.Enabled = false;
                    }
                }
            }
            else
            {
                pictureBoxBOX4.Image = null;
                pictureBoxBOX4.Tag = "brak";
                --ilePaczek;
                if (ilePaczek < 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Enabled) checkBox.Enabled = true;
                    }
                }
            }
        }
        private void checkBox5_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox5.Checked)
            {
                pictureBoxBOX5.Image = Mobot.Properties.Resources.paczka;
                pictureBoxBOX5.Tag = "paczka";
                ++ilePaczek;
                if (ilePaczek == 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Checked) checkBox.Enabled = false;
                    }
                }
            }
            else
            {
                pictureBoxBOX5.Image = null;
                pictureBoxBOX5.Tag = "brak";
                --ilePaczek;
                if (ilePaczek < 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Enabled) checkBox.Enabled = true;
                    }
                }
            }
        }
        private void checkBox6_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox6.Checked)
            {
                pictureBoxBOX6.Image = Mobot.Properties.Resources.paczka;
                pictureBoxBOX6.Tag = "paczka";
                ++ilePaczek;
                if (ilePaczek == 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Checked) checkBox.Enabled = false;
                    }
                }
            }
            else
            {
                pictureBoxBOX6.Image = null;
                pictureBoxBOX6.Tag = "brak";
                --ilePaczek;
                if (ilePaczek < 3)
                {
                    for (int i = 1; i <= 6; ++i)
                    {
                        CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                        if (!checkBox.Enabled) checkBox.Enabled = true;
                    }
                }
            }
        }
        //zatwierdza rozmieszczenie paczek i zmienia interfejs
        private void buttonRozmiescPaczki_Click(object sender, EventArgs e)
        {
            for (int i = 1; i <= 6; ++i)
            {
                CheckBox checkBox = groupBox1.Controls.Find("checkBox" + i.ToString(), true).FirstOrDefault() as CheckBox;
                //if (checkBox.Checked) obecnoscPaczkiWBoxie[i - 1] = true;
                //else obecnoscPaczkiWBoxie[i - 1] = false;
            }
            pictureBox3.Enabled = true;
            groupBox1.Visible = false;
            listView1.Visible = true;
            buttonWykonaj.Visible = true;
        }

        //służy do obsługi grafiki w trybie automatycznym (dodawanie poleceń metodą podnieś-upuść)
        private void pictureBoxBOX_MouseMove(int ktoryPictureBoxBOX)
        {
            PictureBox pic = this.Controls.Find("pictureBoxBOX" + ktoryPictureBoxBOX.ToString(),true).FirstOrDefault() as PictureBox;
            if (pic.Tag.Equals("paczka") && !podniesionaPaczka)
            {
                pic.Image = Mobot.Properties.Resources.strzalkaWGore;
                pic.Tag = "strzalkaWGore";
            }
            else if (pic.Tag.Equals("brak") && podniesionaPaczka)
            {
                pic.Image = Mobot.Properties.Resources.strzalkaWDol;
                pic.Tag = "strzalkaWDol";
            }
        }
        private void pictureBoxBOX_MouseLeave(int ktoryPictureBoxBOX)
        {
            PictureBox pic = this.Controls.Find("pictureBoxBOX" + ktoryPictureBoxBOX.ToString(), true).FirstOrDefault() as PictureBox;
            if (pic.Tag.Equals("strzalkaWGore"))
            {
                pic.Image = Mobot.Properties.Resources.paczka;
                pic.Tag = "paczka";
            }
            else if (pic.Tag.Equals("strzalkaWDol"))
            {
                pic.Image = null;
                pic.Tag = "brak";

            }
        }
        private void pictureBoxBOX_Click(int ktoryPictureBoxBOX)
        {
            PictureBox pic = this.Controls.Find("pictureBoxBOX" + ktoryPictureBoxBOX.ToString(), true).FirstOrDefault() as PictureBox;
            if (podniesionaPaczka)
            {
                if (pic.Tag.Equals("strzalkaWDol"))
                {
                    podniesionaPaczka = false;
                    pic.Tag = "paczkaStrzalkaWDol";
                    pic.Image = Mobot.Properties.Resources.paczkaStrzalkaWDol;
                    listView1.Items.Add(new ListViewItem(new string[] {skadPobranoPaczke.ToString(),ktoryPictureBoxBOX.ToString()}));
                }

            }
            else if (!podniesionaPaczka)
            {
                if (pic.Tag.Equals("strzalkaWGore"))
                {
                    podniesionaPaczka = true;
                    skadPobranoPaczke = ktoryPictureBoxBOX;
                    pic.Image = Mobot.Properties.Resources.paczkaStrzalkaWGore;
                    pic.Tag = "paczkaStrzalkaWGore";
                }
            }
        }
        private void pictureBoxBOX1_MouseMove(object sender, MouseEventArgs e)
        {
            pictureBoxBOX_MouseMove(1);            
        }
        private void pictureBoxBOX1_MouseLeave(object sender, EventArgs e)
        {
            pictureBoxBOX_MouseLeave(1);
        }
        private void pictureBoxBOX1_Click(object sender, EventArgs e)
        {
            pictureBoxBOX_Click(1);
        }
        private void pictureBoxBOX2_Click(object sender, EventArgs e)
        {
            pictureBoxBOX_Click(2);
        }
        private void pictureBoxBOX2_MouseMove(object sender, MouseEventArgs e)
        {
            pictureBoxBOX_MouseMove(2);
        }
        private void pictureBoxBOX2_MouseLeave(object sender, EventArgs e)
        {
            pictureBoxBOX_MouseLeave(2);
        }
        private void pictureBoxBOX3_Click(object sender, EventArgs e)
        {
            pictureBoxBOX_Click(3);
        }
        private void pictureBoxBOX3_MouseMove(object sender, MouseEventArgs e)
        {
            pictureBoxBOX_MouseMove(3);
        }
        private void pictureBoxBOX3_MouseLeave(object sender, EventArgs e)
        {
            pictureBoxBOX_MouseLeave(3);
        }
        private void pictureBoxBOX4_Click(object sender, EventArgs e)
        {
            pictureBoxBOX_Click(4);
        }
        private void pictureBoxBOX4_MouseMove(object sender, MouseEventArgs e)
        {
            pictureBoxBOX_MouseMove(4);
        }
        private void pictureBoxBOX4_MouseLeave(object sender, EventArgs e)
        {
            pictureBoxBOX_MouseLeave(4);
        }
        private void pictureBoxBOX5_Click(object sender, EventArgs e)
        {
            pictureBoxBOX_Click(5);
        }
        private void pictureBoxBOX5_MouseMove(object sender, MouseEventArgs e)
        {
            pictureBoxBOX_MouseMove(5);
        }
        private void pictureBoxBOX5_MouseLeave(object sender, EventArgs e)
        {
            pictureBoxBOX_MouseLeave(5);
        }
        private void pictureBoxBOX6_Click(object sender, EventArgs e)
        {
            pictureBoxBOX_Click(6);
        }
        private void pictureBoxBOX6_MouseMove(object sender, MouseEventArgs e)
        {
            pictureBoxBOX_MouseMove(6);
        }

        private void pictureBoxBOX6_MouseLeave(object sender, EventArgs e)
        {
            pictureBoxBOX_MouseLeave(6);
        }

        private void buttonWykonaj_Click(object sender, EventArgs e)
        {
            //przycisk wykonaj, jedzie do stanowiska z ktorego ma pobrac paczke
            if (listView1.Items.Count > 0)
            {
                sendThread = new Thread(new ParameterizedThreadStart(Send));
                sendThread.Start(Convert.ToInt16(listView1.TopItem.Text));
                pictureBox3.Enabled = false;

                groupBox4.Enabled = false;
                buttonWykonaj.Enabled = false;
            }

        }

        private void buttonReczny_Click(object sender, EventArgs e)
        {
            // tryb ręczny
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(9);
            pictureBox3.Enabled = false;
            groupBox1.Enabled = false;
            groupBox2.Enabled = true;
            groupBox5.Enabled = false;

            groupBox1.Visible = true;
            listView1.Items.Clear();
            buttonWykonaj.Visible = false;
            buttonReczny.Enabled = false;
            buttonAutomatyczny.Enabled = true;

            listView1.Visible = false;

            buttonTestowy.Enabled = true;


            checkBox1.Checked = false;
            checkBox2.Checked = false;
            checkBox3.Checked = false;
            checkBox4.Checked = false;
            checkBox5.Checked = false;
            checkBox6.Checked = false;

            toolStripStatusLabel4.Text = "Ręczny";

            pictureBoxSkrzyzowanie1.Visible = false;
            pictureBoxSkrzyzowanie2.Visible = false;
            pictureBoxSkrzyzowanie3.Visible = false;

            pictureBoxBOX1.Tag = "brak";
            pictureBoxBOX1.Image = null;
            pictureBoxBOX2.Tag = "brak";
            pictureBoxBOX2.Image = null;
            pictureBoxBOX3.Tag = "brak";
            pictureBoxBOX3.Image = null;
            pictureBoxBOX4.Tag = "brak";
            pictureBoxBOX4.Image = null;
            pictureBoxBOX5.Tag = "brak";
            pictureBoxBOX5.Image = null;
            pictureBoxBOX6.Tag = "brak";
            pictureBoxBOX6.Image = null;
        }

        private void buttonAutomatyczny_Click(object sender, EventArgs e)
        {  
            //tryb automatyczny
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(10);
            pictureBox3.Enabled = true;
            groupBox1.Enabled = true;
            groupBox2.Enabled = false;
            groupBox5.Enabled = false;

            buttonReczny.Enabled = true;
            buttonAutomatyczny.Enabled = false;
            buttonTestowy.Enabled = true;

            toolStripStatusLabel4.Text = "Automatyczny";

            pictureBoxSkrzyzowanie1.Visible = false;
            pictureBoxSkrzyzowanie2.Visible = false;
            pictureBoxSkrzyzowanie3.Visible = false;

            pictureBoxBOX1.Tag = "brak";
            pictureBoxBOX1.Image = null;
            pictureBoxBOX2.Tag = "brak";
            pictureBoxBOX2.Image = null;
            pictureBoxBOX3.Tag = "brak";
            pictureBoxBOX3.Image = null;
            pictureBoxBOX4.Tag = "brak";
            pictureBoxBOX4.Image = null;
            pictureBoxBOX5.Tag = "brak";
            pictureBoxBOX5.Image = null;
            pictureBoxBOX6.Tag = "brak";
            pictureBoxBOX6.Image = null;
        }

        private void buttonTestowy_Click(object sender, EventArgs e)
        {
            //tryb testowy
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(11);

            pictureBox3.Enabled = false;
            groupBox1.Enabled = false;
            groupBox2.Enabled = false;
            groupBox5.Enabled = true;
            checkBox1.Checked = false;
            checkBox2.Checked = false;
            checkBox3.Checked = false;
            checkBox4.Checked = false;
            checkBox5.Checked = false;
            checkBox6.Checked = false;
            groupBox1.Visible = true;
            listView1.Items.Clear();
            buttonWykonaj.Visible = false;
            listView1.Visible = false;
            buttonReczny.Enabled = true;
            buttonAutomatyczny.Enabled = true;
            buttonTestowy.Enabled = false;
            buttonStartPomiar.Enabled = true;

            toolStripStatusLabel4.Text = "Testowy";

            pictureBoxSkrzyzowanie1.Visible = false;
            pictureBoxSkrzyzowanie2.Visible = false;
            pictureBoxSkrzyzowanie3.Visible = false;

            pictureBoxBOX1.Tag = "brak";
            pictureBoxBOX1.Image = null;
            pictureBoxBOX2.Tag = "brak";
            pictureBoxBOX2.Image = null;
            pictureBoxBOX3.Tag = "brak";
            pictureBoxBOX3.Image = null;
            pictureBoxBOX4.Tag = "brak";
            pictureBoxBOX4.Image = null;
            pictureBoxBOX5.Tag = "brak";
            pictureBoxBOX5.Image = null;
            pictureBoxBOX6.Tag = "brak";
            pictureBoxBOX6.Image = null;
        }

        private void buttonStartPomiar_Click(object sender, EventArgs e)
        {
            label6.Text = "Sredni prąd na lewym mostku: -";
            label7.Text = "Sredni prąd na prawym mostku: -";
            label8.Text = "Czas przejazdu: -";
            label9.Text = "Średnia prędkość: -";

            domainUpDown1.Enabled = false;
            buttonStartPomiar.Enabled = false;
            pomiary = new StreamWriter("pomiary_" + domainUpDown1.Text + "_PWM.csv", false);
            pomiary.WriteLine("Prad lewego mostka [mA];Prad prawego mostka [mA];Czas [ms]");
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(12);

            czasstart = DateTime.Now; //zapamietanie czasu startu pomiaru

        }

        private void buttonPowrotDoStartu_Click(object sender, EventArgs e)
        {
            sendThread = new Thread(new ParameterizedThreadStart(Send));
            sendThread.Start(13);
        }     
    }
}
