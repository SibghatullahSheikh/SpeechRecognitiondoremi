using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Kinect;
using Microsoft.Speech.AudioFormat;
using Microsoft.Speech.Recognition;

using WebSocket4Net;

namespace sender_csharp
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        //[要変更]ここにWebsocket プロキシサーバのURLをセットします。
        private string serverURL = "ws://kgr.cs.inf.shizuoka.ac.jp:10808/";
        //[要変更]ここにチャンネル文字列（半角英数字・ブラウザ側と同じ文字列）をセットします
        private string channel = "cs12084";
        private WebSocket websocket;
        private bool ready = false;
        public string status = "logout";

        ColorImageFormat rgbFormat = ColorImageFormat.RgbResolution640x480Fps30;
        private byte[] pixelBuffer = null;
        private Skeleton[] skeletonBuffer = null;
        private RenderTargetBitmap bmpBuffer = null;
        private BitmapImage maskImage = null;
        private DrawingVisual drawVisual = new DrawingVisual();

        /// <summary>
        /// Active Kinect sensor.
        /// </summary>
        private KinectSensor sensor;

        /// <summary>
        /// Speech recognition engine using audio data from Kinect.
        /// </summary>
        private SpeechRecognitionEngine speechEngine;
        
        public MainWindow()
        {
            InitializeComponent();
            if (serverURL == "")
            {
                textBox1.Text = "URL不明！";
            }
            else
            {
                textBox1.Text = channel;
                websocket = new WebSocket(serverURL);
                websocket.Closed += new EventHandler(websocket_Closed);
                websocket.Error += new EventHandler<SuperSocket.ClientEngine.ErrorEventArgs>(websocket_Error);
                websocket.MessageReceived += new EventHandler<MessageReceivedEventArgs>(websocket_MessageReceived);
                websocket.Opened += new EventHandler(websocket_Opened);
                websocket.Open();
            }
        }

        //WebSocketで文字列を送信するメソッド
        private void sendMessage(string cmd, string msg)
        {
            if (ready)
            {
                //channelを先頭に付けて送信
                websocket.Send(channel + ":" + cmd + "," + msg);
            }
        }

        private void websocket_Opened(object sender, EventArgs e)
        {
            //以下のブロックはスレッドセーフにGUIを扱うおまじない
            this.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() =>
            {
                //ここにGUI関連の処理を書く。
                button1.IsEnabled = true;
                textBlock2.Text = "接続完了";
            }));

        }

        private void websocket_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            ready = false;
            //以下のブロックはスレッドセーフにGUIを扱うおまじない
            this.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() =>
            {
                //ここにGUI関連の処理を書く。
                textBlock2.Text = "未接続";
                textBlock3.Text = "[error] " + e.Exception.Message + "\n";
                button1.IsEnabled = false;
            }));
            MessageBox.Show("予期しないエラーで通信が途絶しました。再接続には起動しなおしてください。");
        }

        private void websocket_Closed(object sender, EventArgs e)
        {
            ready = false;
            //以下のブロックはスレッドセーフにGUIを扱うおまじない
            this.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() =>
            {
                //ここにGUI関連の処理を書く。
                textBlock2.Text = "未接続";
                textBlock3.Text = "[closed]\n";
                button1.IsEnabled = false;
            }));
            MessageBox.Show("サーバがコネクションを切断しました。");
        }

        private void websocket_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            //  データ受信(サーバで当該チャンネルのモノのみ送る処理をしているが一応チェック)
            if (e.Message.IndexOf(channel+":") == 0) 
            {
                //チャンネル名などをメッセージから削除
                string msg = e.Message.Substring(e.Message.IndexOf(":")+1);
                //カンマ区切りを配列にする方法は↓
                string[] fields = msg.Split(new char[] { ',' });
                this.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() =>
                {
                    //ここにGUI関連の処理を書く。
                    //配列をループで回してスラッシュを付けて表示
                    textBlock3.Text = "";
                    foreach (string field in fields) {
                        textBlock3.Text += field + "/";
                    }
                }));

            }
            else if (e.Message == channel + ";") 
            {
                //ハンドシェイク完了の受信
                ready = true;
                this.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() =>
                {
                    textBlock2.Text = "ハンドシェイク完了";
                    button1.IsEnabled = false;
                }));
            }
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(textBox1.Text, @"^[a-zA-Z0-9]+$"))
            {
                button1.IsEnabled = false;
                channel = textBox1.Text;
                //ハンドシェイクを送信
                websocket.Send(channel + ":");
            }
            else {
                MessageBox.Show("チャンネルは半角英数字のみ！");
            }
        }

        private static RecognizerInfo GetKinectRecognizer()
        {
            foreach (RecognizerInfo recognizer in SpeechRecognitionEngine.InstalledRecognizers())
            {
                string value;
                recognizer.AdditionalInfo.TryGetValue("Kinect", out value);
                if ("True".Equals(value, StringComparison.OrdinalIgnoreCase) && "ja-JP".Equals(recognizer.Culture.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return recognizer;
                }
            }

            return null;
        }

        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            // Look through all sensors and start the first connected one.
            // This requires that a Kinect is connected at the time of app startup.
            // To make your app robust against plug/unplug, 
            // it is recommended to use KinectSensorChooser provided in Microsoft.Kinect.Toolkit
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }


            if (null != this.sensor)
            {

                Uri imgUri = new Uri("pack://application:,,,/images/warai_flat.png");
                maskImage = new BitmapImage(imgUri);
                ColorImageStream clrStream = this.sensor.ColorStream;
                clrStream.Enable(rgbFormat);
                SkeletonStream skelStream = this.sensor.SkeletonStream;
                skelStream.Enable();
                pixelBuffer = new byte[clrStream.FramePixelDataLength];
                skeletonBuffer = new Skeleton[skelStream.FrameSkeletonArrayLength];
                bmpBuffer = new RenderTargetBitmap(clrStream.FrameWidth, clrStream.FrameHeight, 96, 96, PixelFormats.Default);
                rgbImage.Source = bmpBuffer;
                this.sensor.AllFramesReady += AllFramesReady;

                try
                {
                    // Start the sensor!
                    this.sensor.Start();
                }
                catch (IOException)
                {
                    // Some other application is streaming from the same Kinect sensor
                    this.sensor = null;
                }
            }

            if (null == this.sensor)
            {
                this.textBlock3.Text = "Kinect の準備ができてません";
                return;
            }

            RecognizerInfo ri = GetKinectRecognizer();

            if (null != ri)
            {
                // 認識用の辞書を登録する
                this.speechEngine = new SpeechRecognitionEngine(ri.Id);
                var directions = new Choices();     //  よみ　コマンド名
                directions.Add(new SemanticResultValue("ぴあの", "piano"));
                directions.Add(new SemanticResultValue("にげろ", "escape"));
                directions.Add(new SemanticResultValue("こんにちは", "hello"));
                directions.Add(new SemanticResultValue("ど", "do"));
                directions.Add(new SemanticResultValue("れ", "re"));
                directions.Add(new SemanticResultValue("み", "mi"));
                directions.Add(new SemanticResultValue("ふぁ", "fa"));
                directions.Add(new SemanticResultValue("そ", "so"));
                directions.Add(new SemanticResultValue("ら", "ra"));
                directions.Add(new SemanticResultValue("し", "si"));
                directions.Add(new SemanticResultValue("もどる", "return"));
                directions.Add(new SemanticResultValue("ありがとう", "thankyou"));
                var gb = new GrammarBuilder { Culture = ri.Culture };
                gb.Append(directions);

                var g = new Grammar(gb);
                speechEngine.LoadGrammar(g);

                speechEngine.SpeechRecognized += SpeechRecognized;
                speechEngine.SpeechRecognitionRejected += SpeechRejected;

                speechEngine.SetInputToAudioStream(
                    sensor.AudioSource.Start(), new SpeechAudioFormatInfo(EncodingFormat.Pcm, 16000, 16, 1, 32000, 2, null));
                speechEngine.RecognizeAsync(RecognizeMode.Multiple);
            }
            else
            {
                this.textBlock3.Text = "Kinect 音声認識の初期化に失敗しました";
            }
        }

        /// <summary>
        /// Execute uninitialization tasks.
        /// </summary>
        /// <param name="sender">object sending the event.</param>
        /// <param name="e">event arguments.</param>
        private void WindowClosing(object sender, CancelEventArgs e)
        {
            if (null != this.sensor)
            {
                this.sensor.AudioSource.Stop();

                this.sensor.Stop();
                this.sensor = null;
            }

            if (null != this.speechEngine)
            {
                this.speechEngine.SpeechRecognized -= SpeechRecognized;
                this.speechEngine.SpeechRecognitionRejected -= SpeechRejected;
                this.speechEngine.RecognizeAsyncStop();
            }
        }
        /// <summary>
        /// Handler for recognized speech events.
        /// </summary>
        /// <param name="sender">object sending the event.</param>
        /// <param name="e">event arguments.</param>
        /// 

/*        private void ColorImageReady(object sender, ColorImageFrameReadyEventArgs e)
        {
            using (ColorImageFrame imageFrame = e.OpenColorImageFrame())
            {
                if (imageFrame != null)
                {
                    int frmWidth = imageFrame.Width;
                    int frmHeight = imageFrame.Height;
                    imageFrame.CopyPixelDataTo(pixelBuffer);
                    Int32Rect src = new Int32Rect(0, 0, frmWidth, frmHeight);
                    bmpBuffer.WritePixels(src, pixelBuffer, frmWidth * 4, 0);
                }
            }
        }
*/

        private void AllFramesReady(object sender, AllFramesReadyEventArgs e)
        {
            KinectSensor kinect = sender as KinectSensor;
            List<SkeletonPoint> headList = null;
            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
/*                if (skeletonFrame == null && this.skeletonFrament != null)
                {
                    this.textBlock5.Text = "logout";
                    sendMessage("recog", "logout");
                }
                if (skeletonFrame != null && this.skeletonFrament == null)
                {
                    this.textBlock5.Text = "login";
                    sendMessage("recog", "login");
               }
                this.skeletonFrament = skeletonFrame;
*/                if (skeletonFrame != null)
                    headList = getHeadPoints(skeletonFrame);
            }
            using (ColorImageFrame imageFrame = e.OpenColorImageFrame())
            {
                if (imageFrame != null)
                    fillBitmap(kinect, imageFrame, headList);
            }
        }

        private List<SkeletonPoint> getHeadPoints(SkeletonFrame skelFrame)
        {
            List<SkeletonPoint> results = new List<SkeletonPoint>();
            skelFrame.CopySkeletonDataTo(skeletonBuffer);
            foreach (Skeleton skeleton in skeletonBuffer)
            {
                if (skeleton.TrackingState != SkeletonTrackingState.Tracked)
                {
                    continue;
                }
                Joint head = skeleton.Joints[JointType.Head];
                if (head.TrackingState != JointTrackingState.Tracked && head.TrackingState != JointTrackingState.Inferred)
                {
                    int similer = this.status.CompareTo("logout");
                    if (similer != 0)
                    {
                        this.textBlock5.Text = "logout";
                        sendMessage("recog", "logout");
                        this.status = "logout";
                    }
                    continue;
                }
                int similer1 = this.status.CompareTo("login");
                if (similer1 != 0)
                {
                    this.textBlock5.Text = "login";
                    sendMessage("recog", "login");
                    this.status = "login";
                }
                results.Add(head.Position);
            }
            return results;
        }

        private void fillBitmap(KinectSensor Kinect, ColorImageFrame imgFrame, List<SkeletonPoint> headList)
        {
            var drawContext = drawVisual.RenderOpen();
            int frmWidth = imgFrame.Width;
            int frmHeight = imgFrame.Height;
            imgFrame.CopyPixelDataTo(pixelBuffer);
            var bgImg = new WriteableBitmap(frmWidth, frmHeight, 96, 96, PixelFormats.Bgr32, null);
            bgImg.WritePixels(new Int32Rect(0, 0, frmWidth, frmHeight), pixelBuffer, frmWidth * 4, 0);
            drawContext.DrawImage(bgImg, new Rect(0,0,frmWidth,frmHeight));
            for (int idx = 0; headList != null && idx < headList.Count; ++idx)
            {
                ColorImagePoint headPt
                    = Kinect.CoordinateMapper.MapSkeletonPointToColorPoint(headList[idx], rgbFormat);
                Rect rect = new Rect(headPt.X - 64, headPt.Y - 64, 128, 128);
                drawContext.DrawImage(maskImage, rect);
            }
            drawContext.Close();
            bmpBuffer.Render(drawVisual);
        }

        private void SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            // Speech utterance confidence below which we treat speech as if it hadn't been heard
            const double ConfidenceThreshold = 0.25;

            this.textBlock5.Text = "";

            if (e.Result.Confidence >= ConfidenceThreshold)
            {
                String command = e.Result.Semantics.Value.ToString();
                switch (command)
                {
                        // 認識された結果を WebSocket で送信する
                    case "escape":
                    case "hello":
                    case "do":
                    case "re":
                    case "mi":
                    case "fa":
                    case "so":
                    case "ra":
                    case "si":
                    case "return":
                    case "thankyou":
                        this.textBlock5.Text = command;
                        sendMessage("recog", command);
                        break;
                }
            }
        }

        /// <summary>
        /// Handler for rejected speech events.
        /// </summary>
        /// <param name="sender">object sending the event.</param>
        /// <param name="e">event arguments.</param>
        private void SpeechRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            this.textBlock5.Text = "";
        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {
            this.sensor.ElevationAngle = 20; //20度に設定
        }

        private void button3_Click(object sender, RoutedEventArgs e)
        {
            this.sensor.ElevationAngle = 0; //0度に設定
        }

        private void button4_Click(object sender, RoutedEventArgs e)
        {
            this.sensor.ElevationAngle = -20; //-20度に設定
        }

    }
}
