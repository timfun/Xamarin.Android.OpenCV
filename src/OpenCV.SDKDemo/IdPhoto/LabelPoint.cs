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

using OpenCV.Core;

namespace OpenCV.SDKDemo.IdPhoto
{
    public struct LabelPoint
    {
        public int X;
        public int Y;
        public int Label;
    }

    public struct ScalarTuple
    {
        public Scalar fore;
        public Scalar back;

        public double SigmaFore;
        public double SigmaBack;

        public int flag;
    }

    public struct FTuple
    {
        public Scalar fore;
        public Scalar back;
        public double alhpa;
        public double confidence;
    }
}