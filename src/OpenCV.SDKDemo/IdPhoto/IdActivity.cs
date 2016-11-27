using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Util;
using Android.Graphics;

using OpenCV.Android;
using OpenCV.Core;
using OpenCV.ImgProc;
using Size = OpenCV.Core.Size;
using Java.Util;

namespace OpenCV.SDKDemo.IdPhoto
{
    [Activity(Label = "IdActivity")]
    public class IdActivity : Activity
    {
        private IMenuItem _itemPickPhoto;
        //private IMenuItem _itemStartNewGame;
        public  const string Tag = "IdActivity";

        private Mat _frame;
        private Mat _back;
        private Mat _fore;

        private Callback mLoaderCallback;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Create your application here
            Log.Debug(Tag, "Creating and setting view");
            SetContentView(Resource.Layout.IdPhoto);
            mLoaderCallback = new Callback(this);

        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            _itemPickPhoto = menu.Add("choose photo");

            return base.OnCreateOptionsMenu(menu);
        }

        protected override void OnResume()
        {
            base.OnResume();
            if (!OpenCVLoader.InitDebug())
            {
                Log.Debug(Tag, "Internal OpenCV library not found. Using OpenCV Manager for initialization");
                OpenCVLoader.InitAsync(OpenCVLoader.OpencvVersion300, this, mLoaderCallback);
            }
            else
            {
                Log.Debug(Tag, "OpenCV library found inside package. Using it!");
                mLoaderCallback.OnManagerConnected(LoaderCallbackInterface.Success);
            }
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            Log.Info(Tag, "Menu Item selected " + item);

            if (item == _itemPickPhoto)
            {
                var file = "/sdcard/dcim/Camera/IMG_20161125_222542-1-1.jpg";

                var photoImage = FindViewById<ImageView>(Resource.Id.photo);
                var image = BitmapFactory.DecodeFile(file);
                photoImage.SetImageBitmap(image);

                var w = image.Width;
                var h = image.Height;

                _frame = new Mat(image.Width, image.Height, CvType.Cv8uc1);
                var gray = new Mat(image.Width, image.Height, CvType.Cv8uc1);
                _fore = new Mat(image.Width, image.Height, CvType.Cv8uc1);

                OpenCV.Android.Utils.BitmapToMat(image, _frame);
                Imgproc.CvtColor(_frame, gray, Imgproc.ColorRgb2gray);
                //OpenCV.Android.Utils.MatToBitmap(gray, image);
                //photoImage.SetImageBitmap(image);

                var rgbaInnerWindow = new Mat(image.Width, image.Height, CvType.Cv8uc1);

                //Imgproc.Canny(gray, rgbaInnerWindow, 110, 120);

                //Imgproc.Threshold(gray, rgbaInnerWindow, 0, 255, Imgproc.ThreshOtsu | Imgproc.ThreshBinary);
                Imgproc.Threshold(gray, rgbaInnerWindow, 168, 255,  Imgproc.ThreshBinary);

                //OpenCV.Android.Utils.MatToBitmap(rgbaInnerWindow, image);
                //photoImage.SetImageBitmap(image);
                //return true;

                //Imgproc.AdaptiveThreshold(gray, rgbaInnerWindow, 255, Imgproc.AdaptiveThreshGaussianC, Imgproc.ThreshBinary, 3, 3);

                //var leftTop = new Core.Point(0, 0);
                //var leftBottom = new Core.Point(0, h - 1);
                //var rightTop = new Core.Point(w - 1, 0);
                //var rightBottom = new Core.Point(w - 1, h - 1);

                //var white = new Scalar(255, 255, 255);

                //Imgproc.Line(rgbaInnerWindow, leftTop, leftBottom, white, 0);
                ////Imgproc.Line(rgbaInnerWindow, leftTop, rightTop, white, 0);
                //Imgproc.Line(rgbaInnerWindow, leftBottom, rightBottom, white, 0);
                //Imgproc.Line(rgbaInnerWindow, rightTop, rightBottom, white, 0);



                IList<MatOfPoint> contours = new JavaList<MatOfPoint>();
                Mat hierarchy = new Mat();
                var target = rgbaInnerWindow.Clone();
                Imgproc.FindContours(target, contours, hierarchy, Imgproc.RetrExternal, Imgproc.ChainApproxNone);

                //rgbaInnerWindow.

                MatOfPoint max = new MatOfPoint();
                double contour_area_temp = 0, contour_area_max = 0;
                if (contours.Any())
                {
                    foreach (var contour in contours)
                    {
                        contour_area_temp = Math.Abs(Imgproc.ContourArea(contour));
                        if (contour_area_temp > contour_area_max)
                        {
                            contour_area_max = contour_area_temp;
                            max = contour;
                        }
                    }
                }

                var last = new JavaList<MatOfPoint>();
                last.Add(max);

                Imgproc.DrawContours(_frame, last, -1, new Scalar(255, 0, 0), -1);

                OpenCV.Android.Utils.MatToBitmap(_frame, image);
                photoImage.SetImageBitmap(image);

            }
            //else if (item == _itemHideNumbers)
            //{
            //    _puzzle15.ToggleTileNumbers();
            //}

            return base.OnOptionsItemSelected(item);
        }
    }

    class Callback : BaseLoaderCallback
    {
        public Callback(Context context)
            : base(context)
        {

        }

        public override void OnManagerConnected(int status)
        {
            switch (status)
            {
                case LoaderCallbackInterface.Success:
                    {
                        Log.Info(IdActivity.Tag, "OpenCV loaded successfully");
                    }
                    break;
                default:
                    {
                        base.OnManagerConnected(status);
                    }
                    break;
            }
        }
    }
}