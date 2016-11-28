using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Java.Nio.Channels;
using OpenCV.Android;
using OpenCV.Core;

namespace OpenCV.SDKDemo.IdPhoto
{
    public class SharedMatting
    {
        private const string Tag = "SharedMatting";
        private const int KI = 10;          //  D_IMAGE,  for expansion of known region
        private const double KC = 5.0;      //  D_COLOR,  for expansion of known region
        private const int KG = 4;           //  for sample gathering, each unknown p gathers at most kG forground and background samples
        private const int EN = 3;
        private const int EA = 2;
        private const int EB = 4;

        private int height;
        private int width;
        private long step;
        private int channels;

        private int[,] trimapData;
        private int[,] unKnowIndex;
        private int[,] alpha;

        public Mat Image { get; set; }
        public Mat Trimap { get; set; }

        private Mat matte;      // mask

        private List<Point> unknowSet;

        public SharedMatting()
        {
            unknowSet = new List<Point>();    
        }

        public void SetImage(Mat image)
        {
            Image = image;
            height = image.Rows();
            width = image.Cols();
            step = image.Step1();
            channels = image.Channels();

            matte = new Mat(width, height, CvType.Cv8uc1);
        }

        public void SetTrimap(Mat trimap)
        {
            Trimap = trimap;
            trimapData = new int[height,width];

            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    //trimapData[i,j] = p[]
                    var data = trimap.Get(i, j);
                    trimapData[i, j] =(int)data[0];
                }
            }
        }

        public bool Ckeck()
        {
            return true;
        }

        public void SolveAlpha()
        {
            var watch = new Stopwatch();
            watch.Start();
            ExpandKnown();
            watch.Stop();
            Log.Info(Tag, $"ExpandKnown cost Time {watch.ElapsedMilliseconds}");

            watch.Start();
            Gathering();
            watch.Stop();
            Log.Info(Tag, $"Gathering cost Time {watch.ElapsedMilliseconds}");

            RefineSample();

            LocalSmooth();

            GetMatte();
        }

        public void Save(string path)
        {
            
        }

        private void ExpandKnown()
        {
            var labelPoints = new List<LabelPoint>();

            int kc2 = (int)(KC*KC);

            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    if (IsUnKnown(trimapData[i,j]))
                    {
                        var label = -1;
                        var bLabeled = false;

                        var p = new Scalar(Image.Get(i, j));
                        for (var k = 1; (k < KI && !bLabeled); ++k)
                        {
                            int k1 = Math.Max(0, i - k);
                            int k2 = Math.Min(i + k, height - 1);
                            int l1 = Math.Max(0, j - k);
                            int l2 = Math.Min(j + k, width - 1);

                            for (int l = k1; (l <= k2) && !bLabeled; ++l)
                            {
                                double dis;
                                int gray;

                                gray = trimapData[l, l1];
                                if (IsKnown(gray))
                                {
                                    dis = GetPixelDistance(new Point(i, j), new Point(l, l1));
                                    if (dis > KI)
                                    {
                                        continue;
                                    }

                                    var q = new Scalar(Image.Get(l, l1));
                                    var colorDistance = GetColorDistance(p, q);
                                    if (colorDistance <= kc2)
                                    {
                                        bLabeled = true;
                                        label = gray;
                                    }
                                }

                                if (bLabeled)
                                {
                                    break;
                                }

                                gray = trimapData[l, l2];
                                if (IsKnown(gray))
                                {
                                    dis = GetPixelDistance(new Point(i, j), new Point(l, l2));
                                    if (dis > KI)
                                    {
                                        continue;
                                    }

                                    var q = new Scalar(Image.Get(l, l2));
                                    var colorDistance = GetColorDistance(p, q);
                                    if (colorDistance <= kc2)
                                    {
                                        bLabeled = true;
                                        label = gray;
                                    }
                                }
                            }

                            for (int l = l1; l < l2 && !bLabeled; ++l)
                            {
                                double dis;
                                int gray;

                                gray = trimapData[k1, l];
                                if (IsKnown(gray))
                                {
                                    dis = GetPixelDistance(new Point(i, j), new Point(k1, l));
                                    if (dis > KI)
                                    {
                                        continue;
                                    }

                                    var q = new Scalar(Image.Get(k1, l));
                                    var colorDistance = GetColorDistance(p, q);
                                    if (colorDistance <= kc2)
                                    {
                                        bLabeled = true;
                                        label = gray;
                                    }
                                }

                                gray = trimapData[k2, l];
                                if (IsKnown(gray))
                                {
                                    dis = GetPixelDistance(new Point(i, j), new Point(k2, l));
                                    if (dis > KI)
                                    {
                                        continue;
                                    }

                                    var q = new Scalar(Image.Get(k2, l));
                                    var colorDistance = GetColorDistance(p, q);
                                    if (colorDistance <= kc2)
                                    {
                                        bLabeled = true;
                                        label = gray;
                                    }
                                }
                            }
                        }

                        if (label != -1)
                        {
                            labelPoints.Add(new LabelPoint()
                            {
                                X = i,
                                Y = j,
                                Label = label
                            });
                        }
                        else
                        {
                            unknowSet.Add(new Point(i, j));
                        }
                    }
                }
            }
            
            // 更新 trimapData
            foreach (var labelPoint in labelPoints)
            {
                trimapData[labelPoint.X, labelPoint.Y] = labelPoint.Label;
            }
        }

        private void Gathering()
        {
            
        }

        private void RefineSample()
        {
            
        }

        private void LocalSmooth()
        {
            
        }

        private void GetMatte()
        {
            
        }

        private bool IsBackground(int target)
        {
            return target == 0;
        }

        private bool IsForeground(int target)
        {
            return target == 255;
        }

        private bool IsKnown(int target)
        {
            return target == 0 || target == 255;
        }

        private bool IsUnKnown(int target)
        {
            return !IsKnown(target);
        }

        private double GetPixelDistance(Point s, Point d)
        {
            return Math.Sqrt((s.X - d.X)*(s.X - d.X) + (s.Y - d.Y)*(s.Y - d.Y));
        }

        private double GetColorDistance(Scalar source, Scalar target)
        {
            return (source.Val[0] - target.Val[0]) * (source.Val[0] - target.Val[0]) +
                (source.Val[1] - target.Val[1]) * (source.Val[1] - target.Val[1]) +
                (source.Val[2] - target.Val[2]) * (source.Val[2] - target.Val[2]);
        }

        // 采样
        private void Sample(ref List<List<Point>> foregroundSamples, ref List<List<Point>> backgroundSamples)
        {
            foregroundSamples.Clear();
            backgroundSamples.Clear();

            int a = 360/KG;
            int b = (int)(1.7*a/9);
            int angle;

            foreach (var point in unknowSet)
            {
                angle = (int)((point.X + point.Y)*b%a);
                for (int i = 0; i < KG; i++)
                {
                    
                }
            }

            return;
        }
    }
}