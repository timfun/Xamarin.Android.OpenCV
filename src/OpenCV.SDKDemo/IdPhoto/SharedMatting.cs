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
        private List<ScalarTuple> scalarTuples;
        private FTuple[] fTuples;

        public SharedMatting()
        {
            unknowSet = new List<Point>();
            scalarTuples = new List<ScalarTuple>();
        }

        public void SetImage(Mat image)
        {
            Image = image;
            height = image.Rows();
            width = image.Cols();
            step = image.Step1();
            channels = image.Channels();

            matte = new Mat(width, height, CvType.Cv8uc1);

            unKnowIndex = new int[height, width];
        }

        public void SetTrimap(Mat trimap)
        {
            Trimap = trimap;
            trimapData = new int[height,width];

            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
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
            // 保存 matte
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
            List<List<Point>> foregroundSamples = new List<List<Point>>();
            List<List<Point>> backgroundSamples = new List<List<Point>>();

            Sample(ref foregroundSamples, ref backgroundSamples);

            var index = 0;
            var size = unknowSet.Count;

            for (int i = 0; i < size; i++)
            {
                var point = unknowSet[i];
                var foregroundPoints = foregroundSamples[i];
                var backgroundPonits = backgroundSamples[i];
                var probability = CalcProbilityOfForeground(point, ref foregroundPoints, ref backgroundPonits);

                var gmin = 1.0e10;

                Point tf = new Point();
                Point tb = new Point();

                var flag = false;

                foreach (var foregroundPoint in foregroundPoints)
                {
                    var distance = GetPixelDistance(point, foregroundPoint);

                    foreach (var backgroundPoint in backgroundPonits)
                    {
                        var gp = gP(point, foregroundPoint, backgroundPoint, distance, probability);
                        if (gp < gmin)
                        {
                            gmin = gp;
                            tf = foregroundPoint;
                            tb = backgroundPoint;
                            flag = true;
                        }
                    }
                }

                var st = new ScalarTuple();
                st.flag = -1;
                if (flag)
                {
                    st.flag = 1;
                    st.fore = new Scalar(Image.Get((int)tf.X, (int)tf.Y));
                    st.back = new Scalar(Image.Get((int)tb.X, (int)tb.Y));
                    st.SigmaFore = sigma2(tf);
                    st.SigmaFore = sigma2(tb);
                }

                scalarTuples.Add(st);
                unKnowIndex[(int)point.X, (int)point.Y] = index;
                index++;
            }
        }

        private double sigma2(Point point)
        {
            var xi = (int)point.X;
            var yj = (int)point.Y;
            var pc = new Scalar(Image.Get(xi, yj));

            var i1 = Math.Max(0, xi - 2);
            var i2 = Math.Min(xi + 2, height - 1);
            var j1 = Math.Max(0, yj - 2);
            var j2 = Math.Min(yj + 2, width - 1);

            double result = 0;
            int num = 0;

            for (int i = i1; i < i2; i++)   
            {
                for (int j = j1; j < j2; j++)
                {
                    var tempScalar = new Scalar(Image.Get(i, j));
                    result += GetColorDistance(pc, tempScalar);
                    ++num;
                }        
            }

            return result / (num + 1e-10);
        }

        private double gP(Point p, Point fp, Point bp, double distance, double probability)
        {
            Scalar fore = new Scalar(Image.Get((int)fp.X, (int)fp.Y));
            Scalar back = new Scalar(Image.Get((int)bp.X, (int)bp.Y));

            var tn = Math.Pow(CalcNeighborhoodAffinity((int)p.X, (int)p.Y, fore, back), EN);
            var ta = Math.Pow(aP((int)p.X, (int)p.Y, probability, fore, back), EA);
            var tf = distance;
            var tb = Math.Pow(GetPixelDistance(p, bp), EB);

            return tn * ta * tf * tb;
        }

        private double aP(int i, int j, double pf, Scalar fore, Scalar back)
        {
            var c = new Scalar(Image.Get(i, j));
            var alpha = comalpha(c, fore, back);

            return pf + (1 - 2 * pf) * alpha;
        }

        private double CalcNeighborhoodAffinity(int i, int j, Scalar fore, Scalar back)
        {
            var i1 = Math.Max(0, i - 1);
            var i2 = Math.Min(i + 1, height - 1);
            var j1 = Math.Max(0, j - 1);
            var j2 = Math.Min(j + 1, width - 1);

            double result = 0;
            for (int k = i1; k <= i2; k++)
            {
                for (int l = j1; l <= j2; l++)
                {
                    var distortion = 0;
                    result += distortion * distortion;
                }
            }

            return result;
        }

        private double ChromaticDistortion(int i, int j, Scalar fore, Scalar back)
        {
            var scalar = new Scalar(Image.Get(i, j));

            double alpha = comalpha(scalar, fore, back);

            double result = Math.Sqrt((scalar.Val[0] - alpha * fore.Val[0] - (1 - alpha) * back.Val[0]) * (scalar.Val[0] - alpha * fore.Val[0] - (1 - alpha) * back.Val[0]) +
                         (scalar.Val[1] - alpha * fore.Val[1] - (1 - alpha) * back.Val[1]) * (scalar.Val[1] - alpha * fore.Val[1] - (1 - alpha) * back.Val[1]) +
                         (scalar.Val[2] - alpha * fore.Val[2] - (1 - alpha) * back.Val[2]) * (scalar.Val[2] - alpha * fore.Val[2] - (1 - alpha) * back.Val[2]));
            return result / 255.0;        
        }

        private double comalpha(Scalar scalar, Scalar fore, Scalar back)
        {
            double alpha = ((scalar.Val[0] - back.Val[0]) * (fore.Val[0] - back.Val[0]) +
                    (scalar.Val[1] - back.Val[1]) * (fore.Val[1] - back.Val[1]) +
                    (scalar.Val[2] - back.Val[2]) * (fore.Val[2] - back.Val[2]))
                    / ((fore.Val[0] - back.Val[0]) * (fore.Val[0] - back.Val[0]) +
                    (fore.Val[1] - back.Val[1]) * (fore.Val[1] - back.Val[1]) +
                    (fore.Val[2] - back.Val[2]) * (fore.Val[2] - back.Val[2]) + 0.0000001);

            return Math.Min(1.0, Math.Max(0.0, alpha));
        }

        private double CalcProbilityOfForeground(Point point, ref List<Point> foregroundPoints, ref List<Point> backgroundPoints)
        {
            var fmin = 1e10;

            foreach (var item in foregroundPoints)
            {
                var fp = CalcEnergyOfPath(point.X, point.Y, item.X, item.Y);
                if (fp < fmin)
                {
                    fmin = fp;
                }
            }

            var bmin = 1e10;
            foreach (var item in backgroundPoints)
            {
                var bp = CalcEnergyOfPath(point.X, point.Y, item.X, item.Y);
                if (true)
                {
                    bmin = bp;
                }
            }

            return bmin / (fmin + bmin + 1e-10);
        }

        private double CalcEnergyOfPath(double i1, double j1, double i2, double j2)
        {
            var ci = i2 - i1;
            var cj = j2 - j1;
            var z = Math.Sqrt(ci * ci + cj * cj);

            var ei = ci / (z + 0.0000001);
            var ej = cj / (z + 0.0000001);

            var stepinc = Math.Min(1 / (Math.Abs(ei) + 1e-10), 1 / (Math.Abs(ej) + 1e-10));
            var result = 0.0d;

            var pre = new Scalar(Image.Get((int)i1, (int)j1));

            var ti = i1;
            var tj = j1;

            for (double t = 0; ; t += stepinc)
            {
                var inci = ei * t;
                var incj = ej * t;

                int i = (int)(i1 + inci + 0.5);
                int j = (int)(j1 + incj + 0.5);

                //var a = 1.0f;

                var cur = new Scalar(Image.Get(i, j));
                if (ti - i > 0 && tj -j == 0)
                {
                    z = ej;
                }
                else if (ti -i == 0 && tj -j > 0)
                {
                    z = ei;
                }

                result += GetColorDistance(cur, pre) * z;

                pre = cur;
                ti = i;
                tj = j;

                if (Math.Abs(ci) >= Math.Abs(inci) || Math.Abs(cj) >= Math.Abs(incj))
                    break;
            }

            return result;
        }

        private void RefineSample()
        {
            fTuples = new FTuple[width * height +1];
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    var c = new Scalar(Image.Get(i, j));
                    int index = i * width + j;
                    var gray = trimapData[i, j];
                    if (IsBackground(gray))
                    {
                        fTuples[index].fore = c;
                        fTuples[index].back = c;
                        fTuples[index].alhpa = 0;
                        fTuples[index].confidence = 1;
                        alpha[i, j] = 0;
                    }
                    else if (IsForeground(gray))
                    {
                        fTuples[index].fore = c;
                        fTuples[index].back = c;
                        fTuples[index].alhpa = 1;
                        fTuples[index].confidence = 1;
                        alpha[i, j] = 255;
                    }
                }    
            }

            foreach (var point in unknowSet)
            {
                int xi = (int)point.X;
                int yj = (int)point.Y;
                int i1 = Math.Max(0, xi - 5);
                int i2 = Math.Min(xi + 5, height - 1);
                int j1 = Math.Max(0, yj - 5);
                int j2 = Math.Min(yj + 5, width - 1);

                var minvalue = new double[] {1e10, 1e10, 1e10};
                var p = new Point[3];
                int num = 0;
                for (int k = i1; k <= i2; k++)
                {
                    for (int l = j1; l <= j2; l++)
                    {
                        var temp = trimapData[k, l];
                        if (temp == 0 || temp == 255)
                        {
                            continue;
                        }

                        int index = unKnowIndex[k, l];
                        var t = scalarTuples[index];
                        if (t.flag == -1)
                        {
                            continue;
                        }

                        var m = ChromaticDistortion(xi, yj, t.fore, t.back);
                        if (m > minvalue[2])
                        {
                            continue;
                        }

                        if (m < minvalue[0])
                        {
                            minvalue[2] = minvalue[1];
                            p[2] = p[1];

                            minvalue[1] = minvalue[0];
                            p[1] = p[0];

                            minvalue[0] = m;
                            p[0].X = k;
                            p[0].Y = l;

                            num++;
                        }
                        else if (m < minvalue[1])
                        {
                            minvalue[2] = minvalue[1];
                            p[2] = p[1];

                            minvalue[1] = m;
                            p[1].X = k;
                            p[2].X = l;

                            num ++;
                        }
                        else if (m < minvalue[2])
                        {
                            minvalue[2] = m;
                            p[2].X = k;
                            p[2].Y = l;

                            num ++;
                        }
                    }
                }

                num = Math.Min(num, 3);

                double fb = 0;
                double fg = 0;
                double fr = 0;
                double bb = 0;
                double bg = 0;
                double br = 0;
                double sf = 0;
                double sb = 0;

                for (int k = 0; k < num; ++k)
                {
                    int i = unKnowIndex[(int)p[k].X,(int)p[k].Y];
                    fb += scalarTuples[i].fore.Val[0];
                    fg += scalarTuples[i].fore.Val[1];
                    fr += scalarTuples[i].fore.Val[2];
                    bb += scalarTuples[i].back.Val[0];
                    bg += scalarTuples[i].back.Val[1];
                    br += scalarTuples[i].back.Val[2];
                    sf += scalarTuples[i].SigmaFore;
                    sb += scalarTuples[i].SigmaBack;
                }

                fb /= (num + 1e-10);
                fg /= (num + 1e-10);
                fr /= (num + 1e-10);
                bb /= (num + 1e-10);
                bg /= (num + 1e-10);
                br /= (num + 1e-10);
                sf /= (num + 1e-10);
                sb /= (num + 1e-10);

                Scalar fc = new Scalar(fb, fg, fr);
                Scalar bc = new Scalar(bb, bg, br);

                Scalar pc = new Scalar(Image.Get(xi, yj));//LOAD_RGB_SCALAR(data, xi * step + yj * channels);

                double df = GetColorDistance(pc, fc);
                double db = GetColorDistance(pc, bc);
                Scalar tf = fc;
                Scalar tb = bc;

                int idx = xi * width + yj;
                if (df < sf)
                {
                    fc = pc;
                }
                if (db < sb)
                {
                    bc = pc;
                }
                if (fc.Val[0] == bc.Val[0] && fc.Val[1] == bc.Val[1] && fc.Val[2] == bc.Val[2])
                {
                    fTuples[idx].confidence = 0.00000001;
                }
                else
                {
                    fTuples[idx].confidence = Math.Exp(-10 * ChromaticDistortion(xi, yj, tf, tb));
                }


                fTuples[idx].fore = fc;
                fTuples[idx].back = bc;

                fTuples[idx].alhpa = Math.Max(0.0, Math.Min(1.0, comalpha(pc, fc, bc)));
            }

            scalarTuples.Clear();
        }

        private void LocalSmooth()
        {
            double sig2 = 100.0 / (9 * 3.1415926);
            double m = 3 * Math.Sqrt(sig2);

            foreach (var point in unknowSet)
            {
                int xi = (int) point.X;
                int yj = (int) point.Y;

                int i1 = Math.Max(0, (int)(xi - m));
                int i2 = Math.Min((int)(xi + m), height - 1);
                int j1 = Math.Max(0, (int)(yj - m));
                int j2 = Math.Max((int)(yj + m), width - 1);

                int indexp = xi * width + yj;
                FTuple ptuple = fTuples[indexp];

                Scalar wcfsumup = new Scalar(0);
                Scalar wcbsumup = new Scalar(0);
                double wcfsumdown = 0;
                double wcbsumdown = 0;
                double wfbsumup = 0;
                double wfbsundown = 0;
                double wasumup = 0;
                double wasumdown = 0;

                for (int k = i1; k <= i2; ++k)
                {
                    for (int l = j1; l <= j2; ++l)
                    {
                        int indexq = k * width + l;
                        FTuple qtuple = fTuples[indexq];

                        double d = GetPixelDistance(new Point(xi, yj), new Point(k, l));

                        if (d > m)
                        {
                            continue;
                        }

                        double wc;
                        if (d == 0)
                        {
                            wc = Math.Exp(-(d * d) / sig2) * qtuple.confidence;
                        }
                        else
                        {
                            wc = Math.Exp(-(d * d) / sig2) * qtuple.confidence * Math.Abs(qtuple.alhpa - ptuple.alhpa);
                        }
                        wcfsumdown += wc * qtuple.alhpa;
                        wcbsumdown += wc * (1 - qtuple.alhpa);

                        wcfsumup.Val[0] += wc * qtuple.alhpa * qtuple.fore.Val[0];
                        wcfsumup.Val[1] += wc * qtuple.alhpa * qtuple.fore.Val[1];
                        wcfsumup.Val[2] += wc * qtuple.alhpa * qtuple.fore.Val[2];

                        wcbsumup.Val[0] += wc * (1 - qtuple.alhpa) * qtuple.back.Val[0];
                        wcbsumup.Val[1] += wc * (1 - qtuple.alhpa) * qtuple.back.Val[1];
                        wcbsumup.Val[2] += wc * (1 - qtuple.alhpa) * qtuple.back.Val[2];


                        double wfb = qtuple.confidence * qtuple.alhpa * (1 - qtuple.alhpa);
                        wfbsundown += wfb;
                        wfbsumup += wfb * Math.Sqrt(GetColorDistance(qtuple.fore, qtuple.back));

                        double delta = 0;
                        double wa;
                        if (trimapData[k,l] == 0 || trimapData[k,l] == 255)
                        {
                            delta = 1;
                        }
                        wa = qtuple.confidence * Math.Exp(-(d * d) / sig2) + delta;
                        wasumdown += wa;
                        wasumup += wa * qtuple.alhpa;
                    }
                }

                Scalar cp = new Scalar(Image.Get(xi, yj));
                Scalar fp = new Scalar(0);
                Scalar bp = new Scalar(0);

                double dfb;
                double conp;
                double alp;

                bp.Val[0] = Math.Min(255.0, Math.Max(0.0, wcbsumup.Val[0] / (wcbsumdown + 1e-200)));
                bp.Val[1] = Math.Min(255.0, Math.Max(0.0, wcbsumup.Val[1] / (wcbsumdown + 1e-200)));
                bp.Val[2] = Math.Min(255.0, Math.Max(0.0, wcbsumup.Val[2] / (wcbsumdown + 1e-200)));

                fp.Val[0] = Math.Min(255.0, Math.Max(0.0, wcfsumup.Val[0] / (wcfsumdown + 1e-200)));
                fp.Val[1] = Math.Min(255.0, Math.Max(0.0, wcfsumup.Val[1] / (wcfsumdown + 1e-200)));
                fp.Val[2] = Math.Min(255.0, Math.Max(0.0, wcfsumup.Val[2] / (wcfsumdown + 1e-200)));

                //double tempalpha = comalpha(cp, fp, bp);
                dfb = wfbsumup / (wfbsundown + 1e-200);

                conp = Math.Min(1.0, Math.Sqrt(GetColorDistance(fp, bp)) / dfb) * Math.Exp(-10 * ChromaticDistortion(xi, yj, fp, bp));
                alp = wasumup / (wasumdown + 1e-200);

                double alpha_t = conp * comalpha(cp, fp, bp) + (1 - conp) * Math.Max(0.0, Math.Min(alp, 1.0));

                alpha[xi,yj] = (int)(alpha_t * 255);
            }
            fTuples = null;
        }

        private void GetMatte()
        {
            int h = matte.Rows();
            int w = matte.Cols();
            var s = matte.Step1();

            for (int i = 0; i < h; ++i)
            {
                for (int j = 0; j < w; ++j)
                {
                    matte.Put(i, j, alpha[i, j]);
                }
            }
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
            double t;

            var w = Image.Cols();
            var h = Image.Rows();

            Log.Info(Tag, $"unkonwSet count: {unknowSet.Count}");
            var loop = 0;
            foreach (var point in unknowSet)
            {
                var backgroundPoints = new List<Point>();
                var foregroundPoints = new List<Point>();

                loop++;
                if (loop % 100 == 0)
                {
                    Log.Info(Tag, $"loop count {loop}");
                }

                angle = (int)((point.X + point.Y)*b%a);
                for (int i = 0; i < KG; i++)
                {
                    bool f1 = false;
                    bool f2 = false;

                    var z = (angle + i * a) / 180.0f * 3.1415926f;
                    var ex = Math.Sin(z);
                    var ey = Math.Cos(z);
                    var step = Math.Min(1.0f /(Math.Abs(ex) + 1e-10f), 1.0f / (Math.Abs(ey) + 1e-10f));

                    for(t = 0; ; t+= step)
                    {
                        var p = (int)(point.X + ex * t + 0.5f);
                        var q = (int)(point.Y + ex * t + 0.5f);

                        if (p<0 || p>=h || q < 0 || q >= w)
                        {
                            break;
                        }

                        var gray = trimapData[p, q];
                        if (!f1 && IsBackground(gray))
                        {
                            var pt = new Point(p, q);
                            f1 = true;
                            backgroundPoints.Add(pt);
                        }
                        else
                        {
                            if (!f2 && IsForeground(gray))
                            {
                                var pt = new Point(p, q);
                                foregroundPoints.Add(pt);
                                f2 = true;
                            }
                            else
                            {
                                if (f1 && f2)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }

                foregroundSamples.Add(foregroundPoints);
                backgroundSamples.Add(backgroundPoints);
            }
        }
    }
}