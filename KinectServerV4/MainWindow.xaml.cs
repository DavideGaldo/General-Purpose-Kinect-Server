using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Kinect;
using System.Net.Sockets;
using System.Threading;
using QuakeSimulatorNetworkLibrary;

using System.Windows.Threading;
using System.ComponentModel;
using Microsoft.Win32;
using System.IO;

namespace KinectServerV4
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler PropertyChanged;
        public void RaisePropertyChanged(string prop)
        {
            if (PropertyChanged != null) { PropertyChanged(this, new PropertyChangedEventArgs(prop)); }
        }

        Server_Model model = new Server_Model();
        //Network object connesso al dispositivo virtuale
        NetworkObject network_component_vr = new NetworkObject(10000);
        //Network object connesso all'accelerometro sulla tavola
        NetworkObject network_component_acc = new NetworkObject(20000);

        //@TODO Gestire i segnali che arrivano dall'accelerometro






        //True if che client can receive kinect data
        private bool sendDataToRemote = false;  


        //True if the client can receive acceleration data
        private bool sendAccelerationDataToRemote = false;

        //True if the server send random signal
        private bool randomShaking = false;
        
        //Saving path dei dati kinect
        private string savingPath = string.Empty;
        private static string displacementDataXFilePath = "D:\\DisplacementData\\datax1.txt";
        private static string displacementDataYFilePath = "D:\\DisplacementData\\datay1.txt";
        private string[] dispAllDataX = File.ReadAllLines(displacementDataXFilePath);
        private string[] dispAllDataY = File.ReadAllLines(displacementDataYFilePath);
        private float[] dispDataX;
        private float[] dispDataY;
        private int dispDataLength = 0;
        private int dispIndex = 0;


        //Position of the messages in the received network messages
        private int payloadIndex = 1;
        private int messageIndex = 2;
        private int userPositionXIndex = 2;
        private int userPositionYIndex = 3;
        private int userPositionZIndex = 4;
        private int groundPositionXIndex = 5;
        private int groundPositionYIndex = 6;
        private int groundPositionZIndex = 7;


        //Position of the accelerometer received data
        private int accelerometerDataX = 2;
        private int accelerometerDataY = 3;
        private int accelerometerDataZ = 4;










        //Fattore di scala dello spostamento dell'ambiente( dati accelerogramma)
        private int scaleFactor = 20;

        private bool simulationIsRunning = false;

        /// <summary>
        /// Effettua il sottocampionamento del segnale originario da 100Hz a 30Hz circa ( un campione ogni 3)
        /// </summary>
        private void ElaborateData()
        {
            int dimension = dispAllDataX.Length / 3;
            dispDataLength = dimension;
            dispDataX = new float[dimension];
            dispDataY = new float[dimension];

            int j = 0;
            float data = 0f;
            for(int i = 0; i < dispAllDataX.Length && j<dimension ; i=i+3, j++)
            {
                data= float.Parse(dispAllDataX[i], CultureInfo.InvariantCulture);
                dispDataX[j] = data/scaleFactor;
            }

            j = 0;
            for (int i = 0; i < dispAllDataX.Length && j < dimension; i = i + 3, j++)
            {
                data = float.Parse(dispAllDataY[i], CultureInfo.InvariantCulture);
                dispDataY[j] = data / scaleFactor;
            }

            j = 0;

        }

        public MainWindow()
        {
            InitializeComponent();            
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {

            //Elaborate Displacement Data
            ElaborateData();


            //Sottoscrivo evento di streaming rgb
            model.DataUpdated += model_dataUpdated;
            model.PlayerLocked += model_PlayerLocked;
            model.PlayerLost += model_PlayerLost;            
            model.ModelAvailabilityChanged += model_ModelAvailabilityChanged;

            this.network_component_vr.connectionStateChanged += kinect_network_component_connectionStateChanged;
            this.network_component_vr.messageReceived += kinect_network_component_messageReceived;

            this.network_component_acc.connectionStateChanged += accelerometer_network_component_connectionStateChanged;
            this.network_component_acc.messageReceived += accelerometer_network_component_messageReceived;




            if (model.modelIsAvailable)
            {
                //Se il sensore è collegato avvio l'acquisizione dei frame
                lbl_KinectConnection.Content = "Sensor Connected";
                lbl_KinectConnection.Background = Brushes.Green;
                this.model.StartAcquiring();
                if (!this.model.isCalibrated)
                {
                    txt_message.Visibility = System.Windows.Visibility.Visible;
                }
            }
            
            
            //Leggo le grandezze della finsetra Image e del canvas 
            player_identifier.Width = camera_capture.Width;



            //Rimango in attesa per la connessione da parte del client accelerazioni e client Realtà Virtuale
            //Deprecato-> i network component attivano in automatico l'attesa di nuove connessioni all'atto della creazione
            //network_component_vr.waitForTcpConnection();
            //accelerometer_net_component.waitForTcpConnection();


            if (this.network_component_vr.localAddressV4 != null)
            {
                lbl_IpAddress.Background = Brushes.Green;
                lbl_IpAddress.Content = "Ip address: " + this.network_component_vr.localAddressV4.ToString();
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            if (model.isAcquiring)
            {
                model.StopAcquiring();
            }
            if (network_component_vr.remoteIsConnected)
            {
                network_component_vr.SendMessage(NetMessages.disconnectRequest);
                network_component_vr.closeConnection();
            }

            if (network_component_acc.remoteIsConnected)
            {
                network_component_acc.SendMessage(NetMessages.disconnectRequest);
                network_component_acc.closeConnection();
            }

        }
        
        void model_PlayerLost(object source, EventArgs e)
        {
            lbl_user_rec.Content = "User Lost!";
            lbl_user_rec.Background = Brushes.Red;
            txt_message.Visibility = System.Windows.Visibility.Visible;
            txt_message.Text = "Player Lost! Please raise Left or Right arm!";
            if (network_component_vr.remoteIsConnected)
            {
                network_component_vr.SendMessage(NetMessages.userLost);
            }
        }

        void model_PlayerLocked(object source, EventArgs e)
        {
            txt_message.Visibility = System.Windows.Visibility.Hidden;
            lbl_user_rec.Content = "User Recognized!";
            lbl_user_rec.Background = Brushes.Green;
            
        }        

        void model_Calibrated(object source, EventArgs e)
        {
            txt_message.Visibility = System.Windows.Visibility.Hidden;
            txt_message.Text = "";
            

            //Per gestire cambio label alla connessione usare dispatcher
            //Dispatcher.Invoke(new AsyncCallback)

        }        

        void model_ModelAvailabilityChanged(object source, EventArgs e)
        {
            if (model.modelIsAvailable)
            {
                this.model.StartAcquiring();
                lbl_KinectConnection.Content = "Sensor Connected";
                lbl_KinectConnection.Background = Brushes.Green;
                if (!this.model.isCalibrated)
                {
                    txt_message.Visibility = System.Windows.Visibility.Visible;
                }
            }
            else
            {
                this.model.StopAcquiring();
                lbl_KinectConnection.Content = "Sensor Not Connected";
                lbl_KinectConnection.Background = Brushes.Red;
                txt_message.Visibility = System.Windows.Visibility.Hidden;
            }
            //this.Change_Button_State();
        }

        void model_dataUpdated(object source, EventArgs e)
        {
            /*Pulisco i canvas della finestra*/
            player_identifier.Children.Clear();

            camera_capture.Source = model.currentCFrame;
            if (model.playerIsRecognized)
            {
                player_identifier.DrawIdentifier(model.playerIdColorPoint3D, 0);
            }            

            float posX = (float)Math.Round(model.convertedPlayerPosition.X, 2);
            float posY = (float)Math.Round(model.convertedPlayerPosition.Y, 2);
            float posZ = (float)Math.Round(model.convertedPlayerPosition.Z, 2);


            lbl_position.Content = "X: " + posX.ToString() + " Y: " + posY.ToString() + " Z: " + posZ.ToString();
            
            //Invio i dati kinect al client
            if (network_component_vr.remoteIsConnected && this.sendDataToRemote)
            {
                float groundX, groundY, groundZ;
                //network_component_vr.SendKinectData( posX, posY, posZ);
                if (!randomShaking && simulationIsRunning && dispIndex < dispDataLength)
                {
                    groundX = -1 * dispDataY[dispIndex];
                    groundY = 0f;
                    groundZ = dispDataX[dispIndex];
                    dispIndex++;
                }
                else
                {
                    groundX = 0f;
                    groundY = 0f;
                    groundZ = 0f;
                }
                    network_component_vr.SendData(posX, posY, posZ, groundX, groundY, groundZ, true);
            }

            ////Invio i dati accelerogramma al client
            //if (network_component_acc.remoteIsConnected && this.sendAccelerationDataToRemote)
            //{
            //    if (simulationIsRunning && dispIndex<dispDataLength)
            //    {
            //        //Conversione dei sistemi di riferimento, 
            //        //la Y della tavola corrisponde alla X dello spazio virtuale con direzione invertita.
            //        //La X della tavola corrisponde allo Z dello spazio virtuale direzione congruente
            //        ////La Z della tavola corrisponde alla Y dello spazio virtuale, va inviata la dimensione fissa 0f
            //        //invertita
            //        //switch (dispIndex)
            //        //{
            //        //    case 1: //Carico scena outdoor
            //        //        network_component.SendMessage(NetMessages.loadOutdoor);
            //        //        break;
            //        //    case 700: //Prima pausa carico scena indoor
            //        //        network_component.SendMessage(NetMessages.loadIndoor);
            //        //        break;                        
            //        //}                    
            //        //accelerometer_net_component.SendAccelerationData(-1*dispDataY[dispIndex], 0f, dispDataX[dispIndex]);
            //        dispIndex++;
                    
            //    }
            //}
            

        }

        void kinect_network_component_messageReceived(object source, EventArgs e)
        {
            //Implementare la ricezione dei messaggi
            /*Casi possibili:
             * 1)Client segnala disponibilità a ricevere posizione
             * 2)Client segnala disconnessione
             */
            
            //Parsing dei messaggi ricevuti
            string[] dataArray = new string[5];
            

            dataArray = this.network_component_vr.receivedMex.Split((char)124);
            if (dataArray.Length > 2)
            {
                //Se la stringa ricevuta è un messaggio di protocollo
                if (dataArray[payloadIndex] == NetMessages.messagePayload)
                {
                    if (dataArray[messageIndex] == NetMessages.readyToReceive)
                    {
                        //Abilito invio della posizione
                        this.sendDataToRemote = true;
                    }
                    if (dataArray[messageIndex] == NetMessages.disconnectRequest)
                    {
                        this.sendDataToRemote = false;
                        changeVrConnectionLabelStatus(false);
                        network_component_vr.closeConnection();                        
                    }
                }
            }
        }
        
        //Event Handler di connessione al server per i dati kinect
        void kinect_network_component_connectionStateChanged(object source, EventArgs e)
        {
            
            if (network_component_vr.remoteIsConnected)
            {                
                this.sendDataToRemote = true;
                changeVrConnectionLabelStatus(true);
            }
            else
            {                
                this.sendDataToRemote = false;

                changeVrConnectionLabelStatus(false);

                this.network_component_vr.closeConnection();
                this.network_component_vr = null;

                this.network_component_vr = new NetworkObject(10000);
                network_component_vr.connectionStateChanged += kinect_network_component_connectionStateChanged;
                network_component_vr.messageReceived += kinect_network_component_messageReceived;                
            }
        }

        void accelerometer_network_component_messageReceived(object source, EventArgs e)
        {
            //Implementare la ricezione dei messaggi
            /*Casi possibili:
             * 1)Client Invia dati dell'accelerometro
             * 2)Clinet segnala disconnessione
             */

            //Parsing dei messaggi ricevuti
            string[] dataArray;


            dataArray = this.network_component_acc.receivedMex.Split((char)124);
            if (dataArray.Length > 2)
            {
                //Se la stringa ricevuta è un messaggio di protocollo
                if (dataArray[payloadIndex] == NetMessages.messagePayload)
                {
                    if (dataArray[messageIndex] == NetMessages.disconnectRequest)
                    {
                        this.sendAccelerationDataToRemote = false;
                        network_component_acc.closeConnection();
                    }
                }else if(dataArray[payloadIndex] == NetMessages.accelerationPayload)
                {
                    float accX = float.Parse(dataArray[accelerometerDataX], CultureInfo.InvariantCulture.NumberFormat);
                    accX= (float)Math.Round(accX, 4);
                    float accY = float.Parse(dataArray[accelerometerDataY], CultureInfo.InvariantCulture.NumberFormat);
                    accY = (float)Math.Round(accY, 4);
                    float accZ = float.Parse(dataArray[accelerometerDataZ], CultureInfo.InvariantCulture.NumberFormat);
                    accZ = (float)Math.Round(accZ, 4);

                    Application.Current.Dispatcher.BeginInvoke(
                        DispatcherPriority.Render,
                        new Action(() => 
                            this.lbl_Acceleration.Content = "X: " + accX.ToString() + " Y: " + accY.ToString() + " Z: " + accZ.ToString())
                   );
                }
            }
        }

        //Event Handler di connessione al server per i dati kinect
        void accelerometer_network_component_connectionStateChanged(object source, EventArgs e)
        {

            if (network_component_acc.remoteIsConnected)
            {
                this.sendAccelerationDataToRemote= true;
                //Settare qui eventuale cambiamento interfaccia
                changeAccelerometerConnectionLabelStatus(true);
            }
            else
            {
                this.sendAccelerationDataToRemote = false;
                this.network_component_acc.connectionStateChanged -= accelerometer_network_component_connectionStateChanged;
                this.network_component_acc.messageReceived -= accelerometer_network_component_messageReceived;               
                this.network_component_acc.closeConnection();
                this.network_component_acc = null;
                //Settare qui eventuale cambiamento interfaccia
                changeAccelerometerConnectionLabelStatus(false);

                this.network_component_acc = new NetworkObject(20000);
                this.network_component_acc.connectionStateChanged += accelerometer_network_component_connectionStateChanged;
                this.network_component_acc.messageReceived += accelerometer_network_component_messageReceived;
            }
        }

        /// <summary>
        /// This method change the connection label status 
        /// </summary>
        /// <param name="connectionState">True if connected False if disconnected</param>
        void changeVrConnectionLabelStatus(bool connectionState)
        {
            if (connectionState)
            {
                //Uso il dispatcher perchè il metodo viene invocato da un thread differente e ci sono problematiche di proprietà dei 
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() => this.lbl_VisorConnected.Content = "VR Connected"));
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() => this.lbl_VisorConnected.Background = Brushes.Green));
            }else
            {
                //Uso il dispatcher perchè il metodo viene invocato da un thread differente
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() => this.lbl_VisorConnected.Content = "VR NOT CONNECTED"));
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() => this.lbl_VisorConnected.Background = Brushes.Red));
            }
        }


        /// <summary>
        /// This method change the accelerogram connection label status 
        /// </summary>
        /// <param name="connectionState">True if connected False if disconnected</param>
        void changeAccelerometerConnectionLabelStatus(bool connectionState)
        {
            if (connectionState)
            {
                //Uso il dispatcher perchè il metodo viene invocato da un thread differente e ci sono problematiche di proprietà dei 
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() => this.lbl_AccelerationClientConnected.Content = "Acc Client Connected"));
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() => this.lbl_AccelerationClientConnected.Background = Brushes.Green));
            }
            else
            {
                //Uso il dispatcher perchè il metodo viene invocato da un thread differente
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() => this.lbl_AccelerationClientConnected.Content = "ACC CLIENT NOT CONNECTED"));
                Application.Current.Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() => this.lbl_AccelerationClientConnected.Background = Brushes.Red));
            }
        }


        private void LoadIntroLevel(object sender, RoutedEventArgs e)
        {
            //Se il client è collegato mando messaggio  di loading ambiente esterno
            if (this.network_component_vr.remoteIsConnected)
            {
                this.network_component_vr.SendMessage(NetMessages.loadIntro);
            }
        }

        void LoadIndoorLevel(object sender, EventArgs e)
        {
            //Se il client è collegato mando messaggio  di loading ambiente esterno
            if (this.network_component_vr.remoteIsConnected)
            {
                this.network_component_vr.SendMessage(NetMessages.loadIndoor);
            }
        }

        void LoadOutdoorLevel(object sender, EventArgs e)
        {
            //Se il client è collegato mando messaggio di loading ambiente esterno
            if (this.network_component_vr.remoteIsConnected )
            {
                this.network_component_vr.SendMessage(NetMessages.loadOutdoor);
            }
        }

        private void Start_Simulation(object sender, EventArgs e)
        {
            float startingTime = 0.0f;
            float totalDuration = 0.0f;
            float peakTime = 0.0f;
            float peakDuration = 0.0f;
            float peakAmplitude = 0.0f;
            dispIndex = 0;
            sendAccelerationDataToRemote = true;

            #region RandomShaking
            if (EnableRandomShaking.IsChecked == true)
            {
                randomShaking = true;
                #region controllo presenza campi
                //Se uno dei campi da inserire è nullo o zero lancio errore e ritorno
                if (txt_startingTime.Text.Length != 0)
                    startingTime = float.Parse(txt_startingTime.Text, CultureInfo.InvariantCulture.NumberFormat);
                else
                {
                    MessageBox.Show("Errore Starting Time NULLO!");
                    return;
                }
                if (txt_totalDuration.Text.Length != 0)
                    totalDuration = float.Parse(txt_totalDuration.Text, CultureInfo.InvariantCulture.NumberFormat);
                else
                {
                    MessageBox.Show("Errore! Total Duration NULLO!");
                    return;
                }

                if (txt_peakTime.Text.Length != 0)
                {
                    peakTime = float.Parse(txt_peakTime.Text, CultureInfo.InvariantCulture.NumberFormat);
                }
                else
                {
                    MessageBox.Show("Errore! Peak Time Nullo!!");
                    return;
                }

                if (txt_peakDuration.Text.Length != 0)
                {
                    peakDuration = float.Parse(txt_peakDuration.Text, CultureInfo.InvariantCulture.NumberFormat);
                }
                else
                {
                    MessageBox.Show("ERRORE! Peak Duration NULLO!");
                    return;
                }

                if (txt_peakAmplitude.Text.Length != 0)
                {
                    peakAmplitude = float.Parse(txt_peakAmplitude.Text, CultureInfo.InvariantCulture.NumberFormat);
                }
                else
                {
                    MessageBox.Show("ERRORE Peak Amplitude NULLO!");
                    return;
                }
                #endregion

                //Start recording the kinect data
                if (savingPath != string.Empty)
                {
                    model.StartRecording(this.savingPath);
                    btn_StartSimulation.IsEnabled = false;
                    lbl_SimulationStatus.Background = Brushes.Green;
                    lbl_SimulationStatus.Content = "Simulation is RUNNING";
                }
                else
                {
                    MessageBox.Show("Errore! Impostare il saving Path");
                    return;
                }

                //Se il client è collegato mando messaggio di Inizio simulazione
                if (this.network_component_vr.remoteIsConnected)
                {
                    this.network_component_vr.StartSimulationWithParameters(startingTime, totalDuration, peakTime, peakDuration, peakAmplitude);
                }
            }
            #endregion
            #region AccelerometerDataSimulation
            else
            {
                randomShaking = false;
                //Start recording the kinect data
                if (savingPath != string.Empty)
                {
                    model.StartRecording(this.savingPath);
                    btn_StartSimulation.IsEnabled = false;
                    lbl_SimulationStatus.Background= Brushes.Green;
                    lbl_SimulationStatus.Content = "Simulation is RUNNING";
                    simulationIsRunning = true;

                }
                else
                {
                    MessageBox.Show("Errore! Impostare il saving Path");
                    return;
                }
            }
            #endregion


        }


        void StopSimulation(object sender, EventArgs e)
        {            
            //Se il client è collegato mando messaggio  di fine simulazione
            if (this.network_component_vr.remoteIsConnected)
            {
                this.network_component_vr.StopSimulation();
            }

            model.StopRecording();
            sendAccelerationDataToRemote = false;
            savingPath = string.Empty;
            lbl_savingPath.Content = "Saving Path: ";
            btn_StartSimulation.IsEnabled = true;
            lbl_SimulationStatus.Background = Brushes.Red;
            lbl_SimulationStatus.Content = "Simulation is STOPPED";
            simulationIsRunning = false;
            
        }

        private void DisconnectClients(object sender, RoutedEventArgs e)
        {
            //Se il client ha terminato la sua esecuzione e non ha inviato il segnale di disconnessione 
            //cliccando su questo bottone effettuo la disconnessione coatta
            if (this.network_component_vr !=null && this.network_component_vr.remoteIsConnected)
            {
                this.network_component_vr.SendMessage(NetMessages.disconnectRequest);
                this.network_component_vr.closeConnection();
            }

            if (this.network_component_acc != null && this.network_component_acc.remoteIsConnected)
            {
                this.network_component_acc.SendMessage(NetMessages.disconnectRequest);
                this.network_component_acc.closeConnection();
            }
        }

        /// <summary>
        /// Launches the SaveFileDialog window to help user create a new recording file
        /// </summary>
        /// <returns>File path to use when recording a new event file</returns>
        private string SaveRecordingAs()
        {
            string fileName = string.Empty;

            SaveFileDialog dlg = new SaveFileDialog();
            DateTime now = DateTime.Now;
            string basicFileName = now.Year.ToString() +now.Month.ToString() + now.Day.ToString() + "_" + now.Hour.ToString() + now.Minute.ToString();
            dlg.FileName = basicFileName + ".xef";
            dlg.InitialDirectory = model.recordingPath;
            dlg.DefaultExt = ".xef";
            dlg.AddExtension = true;
            dlg.Filter = "KStudio Event File (*.xef, *.xrf)|*.xef;*.xrf";
            dlg.CheckPathExists = true;
            bool? result = dlg.ShowDialog();

            if (result == true)
            {
                fileName = dlg.FileName;
            }
            return fileName;
        }

        private void SetSavingPath(object sender, RoutedEventArgs e)
        {
            savingPath = SaveRecordingAs();
            lbl_savingPath.Content = "Saving Path: "+savingPath; ;
        }





        #region outdated
        //private void StopSimulation(object sender, EventArgs e)
        //{            
        //    //Se il client è collegato mando messaggio  di fine simulazione
        //    if (this.network_component.remoteIsConnected)
        //    {                
        //        this.network_component.StopSimulation();
        //    }
        //}

        //private void Start_Stop_Acquiring(object sender, RoutedEventArgs e)
        //{
        //    if (model.modelIsAvailable&&!model.isAcquiring)
        //    {
        //        model.StartAcquiring();
        //        if (!model.playerIsRecognized)
        //        {
        //            txt_message.Visibility = System.Windows.Visibility.Visible;
        //        }
        //    }
        //    else if(model.modelIsAvailable&&model.isAcquiring)
        //    {
        //        //Se è collegato un client viene disconnessa la connessione
        //        if (network_component.clientIsConnected)
        //        {
        //            network_component.SendMessage(NetMessages.disconnectRequest);
        //        }
        //        //fermo acquisizione
        //        model.StopAcquiring();
        //    }
        //    //cambio lo stato del pulsante
        //    //this.Change_Button_State();
        //}

        //private void Change_Button_State()
        //{
        //    /* Cambia lo stato dei bottoni in relazione alla disponibilità del sensore kinect.
        //     * Se Kinect connessa i bottoni sono cliccabili
        //     * Se Kinect viene disconnessa o non è connessa i bottoni sono disabilitati
        //     * */
        //    if (model.modelIsAvailable)
        //    {
        //        btn_StartStopKinect.IsEnabled = true;
        //    }
        //    else
        //    {
        //        btn_StartStopKinect.IsEnabled = false;
        //    }

        //    if (model.isAcquiring)
        //    {
        //        btn_StartStopKinect.Content = "Stop Kinect Sensor";
        //    }
        //    else
        //    {
        //        btn_StartStopKinect.Content = "Start Kinect Sensor";
        //    }
        //}

        #endregion


    }
}
