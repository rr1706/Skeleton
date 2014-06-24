using Microsoft.Kinect;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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

namespace RatchetSkeleton
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private KinectSensor camera;
        private DrawingGroup canvas;
        private DrawingImage output;
        private readonly Brush lhandBrush = Brushes.Red;
        private readonly Brush rhandBrush = Brushes.Blue;
        private readonly Pen whiteLine = new Pen(Brushes.White, 5);
        private readonly bool ads = true;
        private Point lhandPos = new Point(0, 0);
        private Point rhandPos = new Point(0, 0);
        private Point motorSpeeds = new Point(0, 0);
        private SpecialAction action = SpecialAction.NONE;

        private Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram,
ProtocolType.Udp);
        private readonly IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse("10.17.6.2"), 80);

        public MainWindow()
        {
            InitializeComponent();
        }

        private void window_Loaded(object sender, RoutedEventArgs e)
        {
            foreach (KinectSensor sensor in KinectSensor.KinectSensors)
            {
                if (sensor.Status == KinectStatus.Connected)
                {
                    camera = sensor;
                    break;
                }
            }

            if (camera == null)
            {
                SensorError();
                return;
            }

            try
            {
                camera.SkeletonStream.Enable();
                camera.SkeletonFrameReady += camera_SkeletonFrameReady;
                camera.Start();
            }
            catch (Exception)
            {
                SensorError();
                return;
            }
            this.canvas = new DrawingGroup();
            this.output = new DrawingImage(canvas);
            View.Source = output;
        }

        void SensorError()
        {
            Console.Error.WriteLine("Could not find a kinect sensor");
            Close();
        }

        void camera_SkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            Skeleton[] skeletons;
            using (SkeletonFrame frame = e.OpenSkeletonFrame())
            {
                if (frame == null)
                {
                    return;
                }
                skeletons = new Skeleton[frame.SkeletonArrayLength];
                frame.CopySkeletonDataTo(skeletons);
            }
            foreach (Skeleton skel in skeletons)
            {
                if (skel.TrackingState == SkeletonTrackingState.Tracked)
                {
                    Console.WriteLine("Found a skeleton tracked.");
                    TrackSkeleton(skel);
                    break; // only one skeleton
                }
            }
            using (DrawingContext dc = canvas.Open())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 640, 480));

                // centerline
                dc.DrawLine(whiteLine, new Point(320, 0), new Point(320, 480));

                // right side
                dc.DrawRectangle(Brushes.BlueViolet, whiteLine, new Rect(new Point(320, 120), new Point(640, 360)));
                dc.DrawRectangle(Brushes.Violet, whiteLine, new Rect(new Point(430, 190), new Point(530, 290)));
                dc.DrawText(new FormattedText(String.Format("Speed {0:0.00} {1:0.00}", motorSpeeds.X, motorSpeeds.Y),
                    CultureInfo.GetCultureInfo("en-us"), FlowDirection.LeftToRight,
                    new Typeface("Arial"), 24, Brushes.Yellow), new Point(360, 80));


                // shoot trigger
                dc.DrawRectangle(Brushes.PaleVioletRed, whiteLine, new Rect(new Point(220, 120), new Point(280, 180)));
                dc.DrawText(new FormattedText("Fire", CultureInfo.GetCultureInfo("en-us"), FlowDirection.LeftToRight,
                    new Typeface("Arial"), 24, Brushes.Yellow), new Point(225, 130));

                // intake trigger
                dc.DrawRectangle(Brushes.PaleVioletRed, whiteLine, new Rect(new Point(220, 320), new Point(280, 380)));
                dc.DrawText(new FormattedText("Intake", CultureInfo.GetCultureInfo("en-us"), FlowDirection.LeftToRight,
                    new Typeface("Arial"), 16, Brushes.Yellow), new Point(225, 330));

                dc.DrawText(new FormattedText(action.ToString(), CultureInfo.GetCultureInfo("en-us"), FlowDirection.LeftToRight,
                    new Typeface("Arial"), 24, Brushes.Yellow), new Point(20, 80));


                if (ads)
                {
                    dc.DrawText(new FormattedText("1706", CultureInfo.GetCultureInfo("en-us"),
                        FlowDirection.LeftToRight, new Typeface("Comic Sans MS"), 72, Brushes.Yellow),
                        new Point(40, 120));
                }
                dc.DrawEllipse(this.lhandBrush, null, this.lhandPos, 10, 10);
                dc.DrawEllipse(this.rhandBrush, null, this.rhandPos, 10, 10);
                canvas.ClipGeometry = new RectangleGeometry(new Rect(0, 0, 640, 480));
            }
            UpdateRobot();
        }


        void TrackSkeleton(Skeleton skel)
        {
            Joint lhand = skel.Joints[JointType.HandLeft];
            Joint rhand = skel.Joints[JointType.HandRight];
            if (lhand.TrackingState == JointTrackingState.Inferred && rhand.TrackingState == JointTrackingState.Inferred)
            {
                Console.WriteLine("Warning: positions may be incorrect");
            }
            if (lhand.TrackingState == JointTrackingState.Tracked)
            {
                DepthImagePoint depthPoint = this.camera.CoordinateMapper.MapSkeletonPointToDepthPoint(lhand.Position, DepthImageFormat.Resolution640x480Fps30);
                lhandPos = new Point(depthPoint.X, depthPoint.Y);
                CalcActions();
            }
            if (rhand.TrackingState == JointTrackingState.Tracked)
            {
                DepthImagePoint depthPoint = this.camera.CoordinateMapper.MapSkeletonPointToDepthPoint(rhand.Position, DepthImageFormat.Resolution640x480Fps30);
                rhandPos = new Point(depthPoint.X, depthPoint.Y);
                CalcMotorSpeeds();
            }
            // If a hand can't be tracked, it will send the last applicable value
        }

        void CalcMotorSpeeds()
        {
            if (rhandPos.X < 320 || rhandPos.X > 640 || rhandPos.Y < 120 || rhandPos.Y > 360)
            {
                // Outside of right side controller
                return;
            }
            Point centerRebased = new Point(rhandPos.X - 480, -(rhandPos.Y - 240));
            if (centerRebased.X > -50 && centerRebased.X < 50)
            {
                centerRebased.X = 0;
            }
            if (centerRebased.Y > -50 && centerRebased.Y < 50)
            {
                centerRebased.Y = 0;
            }
            motorSpeeds = new Point();
            if (centerRebased.X < 0)
            {
                motorSpeeds.X = -Math.Sqrt(Math.Abs(centerRebased.X / 160.0));
            }
            else
            {
                motorSpeeds.X = Math.Sqrt(centerRebased.X / 160.0);
            }

            if (centerRebased.Y < 0)
            {
                motorSpeeds.Y = -Math.Sqrt(Math.Abs(centerRebased.Y / 120.0));
            }
            else
            {
                motorSpeeds.Y = Math.Sqrt(centerRebased.Y / 120.0);
            }
        }

        void CalcActions()
        {
            action = SpecialAction.NONE;
            if (lhandPos.X > 220 && lhandPos.X < 280 && lhandPos.Y > 120 && lhandPos.Y < 180)
            {
                action = SpecialAction.FIRE;
            }
            else if (lhandPos.X > 220 && lhandPos.X < 280 && lhandPos.Y > 320 && lhandPos.Y < 380)
            {
                action = SpecialAction.INTAKE;
            }
        }

        void UpdateRobot()
        {
            string text = String.Format("{0:0.00} {1:0.00}", motorSpeeds.X, motorSpeeds.Y);
            byte[] send_buffer = Encoding.ASCII.GetBytes(text);

            sock.SendTo(send_buffer, endPoint);
        }
    }
}
