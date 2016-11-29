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
        private IMenuItem _itemGray;
        private IMenuItem _itemThreshold;
        private IMenuItem _itemFindContours;
        private IMenuItem _itemCreateTrimap;
        private IMenuItem _itemSharedMatting;

        public  const string Tag = "IdActivity";
        private ImageView _imageView;
        private Bitmap _image;

        private Mat _raw;           // 原图
        private Mat _gray;          // 灰度图
        private Mat _threshold;     // 二值图
        private Mat _trimap;        // 三元图
        private Mat _back;      
        private Mat _fore;

        private Callback mLoaderCallback;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            // Create your application here
            Log.Debug(Tag, "Creating and setting view");
            SetContentView(Resource.Layout.IdPhoto);

            _imageView = FindViewById<ImageView>(Resource.Id.photo);

            mLoaderCallback = new Callback(this);
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            _itemPickPhoto = menu.Add("choose photo");
            _itemGray = menu.Add("Gray");
            _itemThreshold = menu.Add("Threshold");
            _itemFindContours = menu.Add("FindContours");
            _itemCreateTrimap = menu.Add("CreateTrimap");
            _itemSharedMatting = menu.Add("SharedMatting");

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

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == 0 && resultCode == Result.Ok)
            {
                _image = BitmapFactory.DecodeStream(ContentResolver.OpenInputStream(data.Data));                
                _imageView.SetImageBitmap(_image);
                _raw = new Mat(_image.Width, _image.Height, CvType.Cv8uc1);
                OpenCV.Android.Utils.BitmapToMat(_image, _raw);
            }

            if (requestCode == 1 && resultCode == Result.Ok)
            {
                _image  = BitmapFactory.DecodeStream(ContentResolver.OpenInputStream(data.Data));
                _imageView.SetImageBitmap(_image);
                _trimap = new Mat(_image.Width, _image.Height, CvType.Cv8uc1);
                OpenCV.Android.Utils.BitmapToMat(_image, _trimap);
            }
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            Log.Info(Tag, "Menu Item selected " + item);

            if (item == _itemPickPhoto)
            {
                var imageIntent = new Intent();
                imageIntent.SetType("image/*");
                imageIntent.SetAction(Intent.ActionGetContent);
                StartActivityForResult(Intent.CreateChooser(imageIntent, "Select photo"), 0);
            }
            else if (item == _itemGray)
            {
                // 灰度图
                _gray = new Mat(_raw.Width(), _raw.Height(), CvType.Cv8uc1);
                Imgproc.CvtColor(_raw, _gray, Imgproc.ColorRgb2gray);
                ShowImage(_gray);
            }
            else if (item == _itemThreshold)
            {
                // 二值化
                _threshold = new Mat(_image.Width, _image.Height, CvType.Cv8uc1);
                Imgproc.Threshold(_gray, _threshold, 168, 255, Imgproc.ThreshBinary);
                ShowImage(_threshold);
            }
            else if (item == _itemFindContours)
            {
                // 查找最大连通区域
                IList<MatOfPoint> contours = new JavaList<MatOfPoint>();
                Mat hierarchy = new Mat();
                var target = _threshold.Clone();
                Imgproc.FindContours(target, contours, hierarchy, Imgproc.RetrExternal, Imgproc.ChainApproxNone);

                MatOfPoint max = new MatOfPoint();
                double contour_area_max = 0;
                if (contours.Any())
                {
                    foreach (var contour in contours)
                    {
                        var contour_area_temp = Math.Abs(Imgproc.ContourArea(contour));
                        if (contour_area_temp > contour_area_max)
                        {
                            contour_area_max = contour_area_temp;
                            max = contour;
                        }
                    }
                }

                var last = new JavaList<MatOfPoint>();
                last.Add(max);

                Imgproc.DrawContours(_raw, last, -1, new Scalar(255, 0, 0));

                ShowImage(_raw);
            }
            else if (item == _itemCreateTrimap)
            {
                // 生成三元图  暂时先用生成的图替代 
                var imageIntent = new Intent();
                imageIntent.SetType("image/*");
                imageIntent.SetAction(Intent.ActionGetContent);
                StartActivityForResult(Intent.CreateChooser(imageIntent, "Select photo"), 1);
            }
            else if(item == _itemSharedMatting)
            {
                // 扣图
                var sharedMatting = new SharedMatting();
                sharedMatting.SetImage(_raw);
                sharedMatting.SetTrimap(_trimap);
                sharedMatting.SolveAlpha();
            }

            return base.OnOptionsItemSelected(item);
        }

        private void ShowImage(Mat mat)
        {
            Android.Utils.MatToBitmap(mat, _image);
            _imageView.SetImageBitmap(_image);
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