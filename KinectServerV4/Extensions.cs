using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Kinect;
using System.Windows.Controls;

namespace KinectServerV4
{
    public static class Extensions
    {

        //Converte un color frame in bitmap per disegnarlo
        public static ImageSource ToBitmap(this ColorFrame frame)
        {
            int width = frame.FrameDescription.Width;
            int height = frame.FrameDescription.Height;
            PixelFormat format = PixelFormats.Bgr32;
            byte[] pixels = new byte[width * height * ((format.BitsPerPixel + 7) / 8)];

            if (frame.RawColorImageFormat == ColorImageFormat.Bgra)
            {
                frame.CopyRawFrameDataToArray(pixels);
            }
            else
            {
                frame.CopyConvertedFrameDataToArray(pixels, ColorImageFormat.Bgra);
            }

            int stride = width * format.BitsPerPixel / 8;

            return BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride);
        }

        //Converte un frame di profondità in bitmap per disegnarlo
        public static ImageSource ToBitmap(this DepthFrame frame)
        {
            int width = frame.FrameDescription.Width;
            int height = frame.FrameDescription.Height;
            PixelFormat format = PixelFormats.Bgr32;

            ushort minDepth = frame.DepthMinReliableDistance;
            ushort maxDepth = frame.DepthMaxReliableDistance;

            ushort[] pixelData = new ushort[width * height];
            byte[] pixels = new byte[width * height * (format.BitsPerPixel + 7) / 8];

            frame.CopyFrameDataToArray(pixelData);

            int colorIndex = 0;
            for (int depthIndex = 0; depthIndex < pixelData.Length; ++depthIndex)
            {
                ushort depth = pixelData[depthIndex];

                byte intensity = (byte)(depth >= minDepth && depth <= maxDepth ? depth : 0);

                pixels[colorIndex++] = intensity; // Blue
                pixels[colorIndex++] = intensity; // Green
                pixels[colorIndex++] = intensity; // Red

                ++colorIndex;
            }

            int stride = width * format.BitsPerPixel / 8;

            return BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride);
        }

        //Disegna l'indentificatore del player sullo stream RGB
        public static void DrawIdentifier(this Canvas canvas, Position3D colorPoint, int playerIndex)
        {
            Color[] pointColors = new Color[6];

            pointColors[0] = Colors.LightBlue;
            pointColors[1] = Colors.Red;
            pointColors[2] = Colors.Green;
            pointColors[3] = Colors.Yellow;
            pointColors[4] = Colors.Blue;
            pointColors[5] = Colors.Violet;

            Ellipse ellipse = new Ellipse
            {
                Width = 40,
                Height = 40,
                Fill = new SolidColorBrush(pointColors[playerIndex])
            };


            TextBlock identifier = new TextBlock
            {
                Width = 40,
                Height = 40,
                Foreground = new SolidColorBrush(Colors.Black),
                TextAlignment = System.Windows.TextAlignment.Center,
                FontSize = 30,
                Text = playerIndex.ToString()

            };


            /* Le coordinate dello spazio RGB sono 1920*1080 mentre quelle del canvas dipendono
             * dalla risoluzione dello schermo, per questo devo convertire le coordinate dallo
             * spazio RGB del sensore a quello del canvas dove disegnero' l'indentificatore 
             * del player*/

            float convertedX = ((float)canvas.ActualWidth * colorPoint.X) / 1920;
            float convertedY = ((float)canvas.ActualHeight * colorPoint.Y) / 1080;
            
            //Controllo se la coordinata non sia infinito( frutto di errore quando il player scompare dal campo visivo kinect)
            if(!float.IsInfinity(convertedX) && !float.IsInfinity(convertedY))
            {
                //Si può disegnare l'identificatore
                //Posiziono l'identificatore del player
                Canvas.SetLeft(ellipse, convertedX - ellipse.Width / 2);
                Canvas.SetTop(ellipse, convertedY - ellipse.Height / 2);
                canvas.Children.Add(ellipse);

                //posiziono il numero identificativo del player
                Canvas.SetLeft(identifier, convertedX - identifier.Width / 2);
                Canvas.SetTop(identifier, convertedY - identifier.Height / 2);
                canvas.Children.Add(identifier);
            }else
            {
                //sollevare errore
            }

            



        }

        
        
    }
}

