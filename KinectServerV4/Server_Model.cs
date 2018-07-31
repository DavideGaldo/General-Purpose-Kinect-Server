using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Kinect;
using Microsoft.Kinect.Tools;
using System.Threading;

namespace KinectServerV4
{

    class Server_Model
    {
        private KinectSensor kinect;                            // Istanza del sensore 
        private MultiSourceFrameReader reader;                  // Il reader dei frame catturati dalla kinect        
        private JointType _trackedJoint = JointType.Neck;       // Giunzione tracciata dalla kinect
        private Body[] bodies = null;                           // Insieme dei corpi identificati dal sensore



        private string _fileExtension = ".xef";
        private string _recordingPath = "D:\\KinectRecordings\\prova\\";       //base path Path used to record data
        public string recordingPath
        {
            get { return _recordingPath; }
            set { _recordingPath = value; }
        }

        private bool _keepRecording = false;
        public bool KeepRecording
        {
            get
            {
                return _keepRecording;
            }

            set
            {
                _keepRecording = value;
            }
        }

        // used to handle the recording of kinect data
        //private static AutoResetEvent stopRecording = new AutoResetEvent(false);





        private bool _isAcquiring = false;                  // Usata per capire se la kinect sta acquisendo frame
        public bool isAcquiring
        {
            get { return _isAcquiring; }
        }



        private bool _modelIsAvailable = false;             // Usata per indicare se il model è disponibile
        public bool modelIsAvailable
        {
            get { return _modelIsAvailable; }
        }

        private ImageSource _currentCFrame;                 //Frame corrente bitmap FullHD acquisito dal sensore
        public ImageSource currentCFrame
        {
            get { return _currentCFrame; }
        }

        private ulong _playerIdentifier;                    // Identificatore del player

        private bool _playerIsRecognized = false;           //Variabile usata per capire quando il player viene identificato
        public bool playerIsRecognized
        {
            get { return _playerIsRecognized; }
        }

        private Position3D _startingPlayerPosition;         //Posizione utente al momento del riconoscimento
        
        private Position3D _currentPlayerPosition;          //Posizione attuale del player nel sistema di riferimento del sensore 
        public Position3D currentPlayerPosition
        {
            get { return _currentPlayerPosition; }
        }

        private ColorSpacePoint _playerIdColorPoint;        //Coordinate a schermo mappate nello spazio colore della kinect dell'identificatore del player
        private Position3D _playerIdColorPoint3D;           //Usata per passare la variabile di sopra da ColorSpacePoint a Position3D
        public Position3D playerIdColorPoint3D
        {
            get { return _playerIdColorPoint3D; }
        }


        private Position3D _convertedPlayerPosition;        //Posizione attuale del player nel nuovo sistema di riferimento dell'area di gioco (origine nel centro ed asse Z invertito)
        public Position3D convertedPlayerPosition
        {
            get { return _convertedPlayerPosition; }
        }

        private bool _isCalibrated = false;                 //Identifica se l'area di gioco è stata calibrata
        public bool isCalibrated
        {
            get { return _isCalibrated; }
        }

        

        private bool _playerLost = false;                   //True se l'utente esce dal raggio d'azione della kinect
        private bool _playerLost_Launched = false;          //True se è stato lanciato evento di player perso




        #region "Events"


        //Evento da lanciare ad ogni aggiornamento dati del model
        public delegate void DataUpdatedEventHandler(object source, EventArgs e);
        public event DataUpdatedEventHandler DataUpdated;

        /// <summary>
        /// Event raised when the model Data is updated
        /// </summary>
        protected virtual void OnDataUpdated()
        {
            if (DataUpdated != null)
            {
                DataUpdated(this, EventArgs.Empty);
            }
        }

        //Evento da lanciare quando la disponibilità del model cambia (esempio quando la kinect viene disconnessa)
        public delegate void ModelAvailabilityChangedEventHandler(object source, EventArgs e);
        public event ModelAvailabilityChangedEventHandler ModelAvailabilityChanged;
        protected virtual void OnModelAvailabilityChanged()
        {
            if (ModelAvailabilityChanged != null)
            {
                ModelAvailabilityChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Evento da lanciare quando il player viene riconosciuto
        /// </summary>
        public delegate void PlayerLockedEventHandler(object source, EventArgs e);
        public event PlayerLockedEventHandler PlayerLocked;
        protected virtual void OnPlayerLocked()
        {
            if (PlayerLocked != null)
            {
                this._playerLost = false;
                this._playerIsRecognized = true;
                PlayerLocked(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Evento la da lanciare quando il player esce dal raggio della kinect
        /// </summary>
        ///
        public delegate void PlayerLostEventHandler(object source, EventArgs e);
        public event PlayerLostEventHandler PlayerLost;
        protected virtual void OnPlayerLost()
        {
            if (PlayerLost != null)
            {
                this._playerLost = true;
                this._playerLost_Launched = true;
                
                PlayerLost(this, EventArgs.Empty);
            }
        }


        //Evento lanciato quando l'area di gioco è stata correttamente calibrata
        public delegate void CalibratedEventHandler(object source, EventArgs e);
        public event CalibratedEventHandler Calibrated;
        protected virtual void OnCalibrated()
        {
            if (Calibrated != null)
            {
                this._isCalibrated = true;
                Calibrated(this, EventArgs.Empty);
            }
        }


        #endregion

        /// <summary>
        /// Delegate to use for placing a job with a single string argument onto the Dispatcher
        /// </summary>
        /// <param name="arg">string argument</param>
        private delegate void OneArgDelegate(string arg);



        //Constructor
        public Server_Model()
        {
            this.kinect = KinectSensor.GetDefault(); //Istanzio il sensore
            this.kinect.Open();                      //Attivo il sensore
            //Sottoscrizione evento disponibilità sensore
            this.kinect.IsAvailableChanged += this.kinect_IsAvailableChanged;
            this._modelIsAvailable = this.kinect.IsAvailable;
        }

        //Avvia acquisizione frame dal sensore kinect
        public void StartAcquiring()
        {
            if (this.kinect != null && !this._isAcquiring)
            {
                //Seleziono i tipi frame da leggere
                this.reader = this.kinect.OpenMultiSourceFrameReader(FrameSourceTypes.Color | FrameSourceTypes.Body);
                //Sottoscrivo l'evento per la gestione dei frame in arrivo
                this.reader.MultiSourceFrameArrived += this.reader_MultiSourceFrameArrived;
                //Imposto lo stato del Model 
                this._isAcquiring = true;

                
            }
        }

        //Ferma acquisizione frame dal sensore kinect
        public void StopAcquiring()
        {
            if (this.kinect != null && this._isAcquiring)
            {
                //Cancello la sottoscrizione all'evento di lettura 
                this.reader.MultiSourceFrameArrived -= this.reader_MultiSourceFrameArrived;
                //Chiudo lo stream della kinect
                this.reader.Dispose();
                this._isAcquiring = false;
            }
        }

        /* Operazioni da intraprendere se il sensore viene scollegato durante l'uso del server
         * lancia evento ModelAvailabilityChanged*/
        void kinect_IsAvailableChanged(object sender, IsAvailableChangedEventArgs e)
        {
            //Se il sensore è instanziato
            if (this.kinect != null)
            {
                //Se la kinect viene scollegata
                if (this.kinect.IsAvailable == false)
                {
                    this.StopAcquiring();
                    this._modelIsAvailable = false;
                }
                else
                {
                    //this.StartAcquiring();
                    this._modelIsAvailable = true;
                }
                
                //Invoco l'evento di cambio disponibilità del model
                OnModelAvailabilityChanged();
            }
        }

        /*  Analisi dei dati provenienti dal sensore, identificazione utente e calibrazione area di gioco 
         *  area di gioco identificata a partire dalla posizione iniziale dell'utente 
         */
        void reader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            MultiSourceFrame frame_reference = e.FrameReference.AcquireFrame();

            /*Prelevo frame RGB*/
            using (ColorFrame frame = frame_reference.ColorFrameReference.AcquireFrame())
            {
                if (frame != null)
                {
                    this._currentCFrame = frame.ToBitmap();
                }
            }

            /*Elaborazione informazioni body Frame*/
            using (BodyFrame bodyFrame = frame_reference.BodyFrameReference.AcquireFrame())
            {
                if (bodyFrame != null)
                {
                    if (bodies == null)
                    {
                        /* Alloco un vettore di dimensioni pari al numero di persone identificate 
                         * all'avvio del sensore
                         */
                        this.bodies = new Body[bodyFrame.BodyCount];
                    }
                    
                    /* Aggiorno le informazioni sui corpi identificati dalla kinect */
                    bodyFrame.GetAndRefreshBodyData(this.bodies);

                    #region Player Lost
                    /* Caso di Player Perso
                     * Potrebbe capitare che il player esca dall'area tracciata dalla kinect, in tal caso va notificato all'utente
                     * di rientrare nell'area di gioco ed alzare il braccio destro o sinistro per essere reintrodotto nel sistema.
                     * Il caso di player perso è identificato dalla variabile _playerLost==True e _playerIsRecognizer==True; 
                     * */

                    //Verifica se tra i corpi tracciati dal sensore è presente il player, verifica effettuata tramite TrackingID
                    if (this._playerIsRecognized)
                    {
                        this._playerLost = true;
                        foreach (Body body in this.bodies)
                        {
                            if (body.IsTracked && body.TrackingId == this._playerIdentifier)
                            {
                                this._playerLost = false;
                                break;
                            }
                        }
                    }

                    //se l'utente è uscito dal campo visivo della kinect
                    if (this._playerLost)
                    {
                        //Se è stato già lanciato l'evento effettuo analisi per identificare di nuovo il player
                        if (this._playerLost_Launched)
                        {
                            foreach (Body body in this.bodies)
                            {
                                if (body.IsTracked)
                                {
                                    //Se utente alza una delle due mani sopra la spalla viene identificato come Player
                                    if (body.Joints[JointType.HandRight].Position.Y > body.Joints[JointType.Head].Position.Y ||
                                        body.Joints[JointType.HandLeft].Position.Y > body.Joints[JointType.Head].Position.Y)
                                    {
                                        this._playerIdentifier = body.TrackingId;
                                        this._playerLost_Launched = false;
                                        this.OnPlayerLocked();
                                    }
                                }
                            }
                        }
                        //Se l'evento di player perso non è stato lanciato, viene lanciato e termina funzione
                        else
                        {
                            this.OnPlayerLost();
                        }
                        //Terminate le operazioni di Player Perso termino notifico aggiornamento dati e ritorno
                        this.OnDataUpdated();
                        return;
                    }


                    #endregion

                    //Player non identificato
                    if (!this._playerIsRecognized)
                    {
                        #region PlayerIdentification
                        foreach (Body body in this.bodies)
                        {
                            if (body.IsTracked)
                            {
                                //Se utente alza entrambe le mani all'altezza delle spalle
                                if (body.Joints[JointType.HandRight].Position.Y > body.Joints[JointType.ElbowRight].Position.Y &&
                                    body.Joints[JointType.HandLeft].Position.Y > body.Joints[JointType.ElbowLeft].Position.Y)
                                {
                                    this._currentPlayerPosition.X = body.Joints[_trackedJoint].Position.X;
                                    this._currentPlayerPosition.Y = body.Joints[_trackedJoint].Position.Y;
                                    this._currentPlayerPosition.Z = body.Joints[_trackedJoint].Position.Z;
                                    //Salvo la posizione iniziale dell'utente, utile in fase di calibrazione
                                    this._startingPlayerPosition = this._currentPlayerPosition;
                                    //Converto coordinate sensore in coordinate RGB 
                                    this._playerIdColorPoint = kinect.CoordinateMapper.MapCameraPointToColorSpace(body.Joints[_trackedJoint].Position);
                                    this._playerIdColorPoint3D.X = _playerIdColorPoint.X;
                                    this._playerIdColorPoint3D.Y = _playerIdColorPoint.Y;
                                    //Il body è identificato come player, salvo il trackingID
                                    this._playerIdentifier = body.TrackingId;
                                    this._playerIsRecognized = true;
                                    //Notifico cambio di stato del player
                                    this.OnPlayerLocked();
                                    //Esco dal ciclo
                                    break;
                                }
                            }
                        }
                        #endregion PlayerIdentification
                    }
                    //Player identificato
                    else
                    {
                        #region Movement Analysis
                        foreach (Body body in bodies)
                        {
                            //Se il corpo è tracciato ed è quello del player allora memorizzo la posizione
                            if (body.IsTracked && body.TrackingId == this._playerIdentifier)
                            {

                                this._currentPlayerPosition.X = body.Joints[_trackedJoint].Position.X;
                                this._currentPlayerPosition.Y = body.Joints[_trackedJoint].Position.Y;
                                this._currentPlayerPosition.Z = body.Joints[_trackedJoint].Position.Z;
                                this._playerIdColorPoint = kinect.CoordinateMapper.MapCameraPointToColorSpace(body.Joints[_trackedJoint].Position);
                                this._playerIdColorPoint3D.X = _playerIdColorPoint.X;
                                this._playerIdColorPoint3D.Y = _playerIdColorPoint.Y;

                                //Conversione coordinata X
                                this._convertedPlayerPosition.X = this._currentPlayerPosition.X - this._startingPlayerPosition.X;                                
                                this._convertedPlayerPosition.Z = this._currentPlayerPosition.Z - this._startingPlayerPosition.Z;


                                /*
                                 * Normalizzo l'altezza del player a 2 metri, l'abbassamento è proporzionale
                                 * questo scalamento risolve il problema dell'altezza del sensore kinect, che influenza l'altezza 
                                 * del personaggio nel mondo virtuale.
                                 */
                                //this._convertedPlayerPosition.Y = (float)Math.Round(((this._currentPlayerPosition.Y * 1.8) / this._startingPlayerPosition.Y), 2, MidpointRounding.AwayFromZero);
                                this._convertedPlayerPosition.Y = 2f + this._currentPlayerPosition.Y - this._startingPlayerPosition.Y;

                                //PUNTO PER INSERIRE EVENTUALI CONTROLLI DA PARTE DELL'UTENTE 

                                //Esco dal ciclo
                                break;
                            }
                        }
                        #endregion
                    }

                    //Lancio Evento dati aggiornati
                    this.OnDataUpdated();
                }
            }
        }

        ///// <summary>
        ///// Function used to store raw data from kinect 
        ///// </summary>
        ///// <param name="filePath">path of the recording file</param>
        ///// <param name="duration">duration of the recording in seconds</param>
        //public void RecordData(string filePath, TimeSpan duration)
        //{
            
        //    using (KStudioClient client = KStudio.CreateClient())
        //    {
        //        client.ConnectToService();

        //        KStudioEventStreamSelectorCollection streamCollection = new KStudioEventStreamSelectorCollection();
        //        streamCollection.Add(KStudioEventStreamDataTypeIds.Ir);
        //        streamCollection.Add(KStudioEventStreamDataTypeIds.Depth);
        //        streamCollection.Add(KStudioEventStreamDataTypeIds.Body);

        //        using (KStudioRecording recording = client.CreateRecording(filePath, streamCollection))
        //        {
                    
        //            recording.StartTimed(duration);
        //            while (recording.State == KStudioRecordingState.Recording)
        //            {
        //                Thread.Sleep(500);
        //            }

        //            if (recording.State == KStudioRecordingState.Error)
        //            {
        //                throw new InvalidOperationException("Error: Recording failed!");
        //            }
        //        }

        //        client.DisconnectFromService();
        //    }
        //}

        /// <summary>
        /// Function used to store raw data from kinect 
        /// </summary>
        /// <param name="filePath">path of the recording file</param>
        public void RecordData(string filePath)
        {
            
            _keepRecording = true;
            using (KStudioClient client = KStudio.CreateClient())
            {
                
                client.ConnectToService();

                KStudioEventStreamSelectorCollection streamCollection = new KStudioEventStreamSelectorCollection();
                streamCollection.Add(KStudioEventStreamDataTypeIds.Ir);
                streamCollection.Add(KStudioEventStreamDataTypeIds.Depth);
                streamCollection.Add(KStudioEventStreamDataTypeIds.Body);
                //streamCollection.Add(KStudioEventStreamDataTypeIds.CompressedColor);

                using (KStudioRecording recording = client.CreateRecording(filePath, streamCollection))
                {

                    recording.Start();


                    //stopRecording.WaitOne();
                    while (_keepRecording)
                    {
                        Thread.Sleep(500);
                    }
                    recording.Stop();

                    if (recording.State == KStudioRecordingState.Error)
                    {
                        throw new InvalidOperationException("Error: Recording failed!");
                    }
                }

                client.DisconnectFromService();
            }
        }

        public void StartRecording(string filePath)
        {
            // Start running the recording asynchronously
            OneArgDelegate recording = new OneArgDelegate(this.RecordData);
            recording.BeginInvoke(filePath, null, null);
        }

        public void StopRecording()
        {
            this._keepRecording = false;
            //stopRecording.Set();
        }


    }

   


}



