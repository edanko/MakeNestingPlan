﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Report.NxlReader
{
    public class Nest
    {
        public Machine Machine { get; set; }
        public Plate Plate { get; set; }
        public List<Remnant> OriginalRemnants { get; set; } = new();
        public List<Part> Parts { get; set; } = new();
        public List<Remnant> Remnants { get; set; } = new();
        public List<DimensionLineAnnotation> DimensionLineAnnotations = new();
        public List<TextProfile> Texts { get; set; } = new();
        public int TextSymbolsCount { get; set; }
        public List<Bridge> Bridges { get; set; } = new();
        public int BridgesCount { get; set; }
        public int RidgesCount { get; set; }

        public void Read(string filename)
        {
            if (!File.Exists(filename))
            {
                return;
            }

            var stream = new FileInfo(filename).OpenRead();
            var input = new GZipStream(stream, CompressionMode.Decompress);
            var doc = XDocument.Load(input);
            var elems = doc.Element("NestFile")?.Element("Nest");

            #region machine

            Machine = new Machine
            {
                Name = elems?.Element("Machine")?.Attribute("Name")?.Value,
                Technology = elems?.Element("Machine")?.Attribute("Technology")?.Value
            };

            #endregion

            #region sheet

            if (elems?.Element("Sheets")?.Element("Plate") != null)
            {
                Plate = new Plate(elems.Element("Sheets")?.Element("Plate"));
            }
            else if (elems?.Element("Sheets")?.Element("Sheet") != null)
            {
                Plate = new Plate(elems.Element("Sheets")?.Element("Sheet"));
            }

            #endregion

            #region OriginalParts

            var originalParts = new List<Part>();

            foreach (var p in elems?.Element("OriginalParts")?.Elements()!)
            {
                switch (p.Name.LocalName)
                {
                    case "Part":
                        var part = new Part
                        {
                            OrderlineInfo = p.Element("DbInfo").Element("ID").Value
                        };

                        foreach (var node in p.Element("Elements").Elements())
                        {
                            if (node.Name.LocalName == "Profile")
                            {
                                var pp = new Profile();
                                pp.Read(node);
                                part.Profiles.Add(pp);
                            }
                        }

                        foreach (var node in p.Element("Texts").Elements())
                        {
                            if (node.Name.LocalName == "TextProfile")
                            {
                                var textProfile = TextProfile.Read(node);
                                part.Texts.Add(textProfile);
                            }
                        }

                        originalParts.Add(part);
                        break;

                    case "Remnant":
                        var rem = new Remnant();

                        foreach (var node in p.Element("Elements").Elements())
                        {
                            if (node.Name.LocalName == "Profile")
                            {
                                var pp = new Profile();
                                pp.Read(node);
                                rem.Profiles.Add(pp);
                            }
                        }

                        foreach (var node in p.Element("Texts").Elements())
                        {
                            if (node.Name.LocalName == "TextProfile")
                            {
                                var textProfile = TextProfile.Read(node);
                                rem.Texts.Add(textProfile);
                            }
                        }

                        OriginalRemnants.Add(rem);
                        break;
                }
            }

            #endregion

            #region PartInfos

            foreach (var pi in elems.Element("PartInfos")?.Elements())
            {
                switch (pi.Name.LocalName)
                {
                    case "PartInfo":
                        var orderlineInfo = pi.Element("DbInfo")?.Element("ID")?.Value;
                        var op = originalParts.Find(x => x.OrderlineInfo == orderlineInfo).DeepCopy();
                        if (op == null)
                        {
                            continue;
                        }

                        var p = new Part
                        {
                            Matrix = Matrix33.Read(pi.Element("Matrix")),
                            Profiles = op.Profiles.ToList(),
                            Texts = op.Texts.ToList()
                        };

                        foreach (var node in pi.Element("Profiles").Elements())
                        {
                            switch (node.Name.LocalName)
                            {
                                case "Profile":
                                {
                                    var profile = new Profile();
                                    profile.ReadPartInfo(node);
                                    p.Profiles.Add(profile);
                                    break;
                                }
                            }
                        }

                        if (pi.Element("DetailId") != null)
                        {
                            p.DetailId = TextProfile.Read(pi.Element("DetailId").Element("TextProfile"));
                        }

                        Parts.Add(p);
                        break;

                    case "RemnantInfo":
                        var rem = new Remnant()
                        {
                            Matrix = Matrix33.Read(pi.Element("Matrix"))
                        };

                        foreach (var node in pi.Element("Profiles")?.Elements())
                        {
                            switch (node.Name.LocalName)
                            {
                                case "Profile":
                                {
                                    var profile = new Profile();
                                    profile.ReadPartInfo(node);
                                    rem.Profiles.Add(profile);
                                    break;
                                }
                            }
                        }

                        Remnants.Add(rem);
                        break;
                }
            }

            foreach (var e in Parts.SelectMany(p => p.Profiles))
            {
                if (e.Tech != null)
                {
                    RidgesCount += e.Tech.RidgesCount;
                }
            }

            #endregion

            #region manipulate

            for (var i = 0; i < Parts.Count; i++)
            {
                //var p = Parts[i];
                var m = Parts[i].Matrix;

                if (m.M[8] < 0.0)
                {
                    foreach (var g in Parts[i].Profiles.SelectMany(e => e.Geometry))
                    {
                        if (g is Arc a)
                        {
                            a.Direction = a.Direction == "CCW" ? "CW" : "CCW";
                        }
                    }
                }

                // foreach (var g in Parts[i].Profiles.SelectMany(e => e.Geometry))
                // {
                //     g.Start = m.TransformPoint(g.Start);
                //     g.End = m.TransformPoint(g.End);
                //     g.Center = m.TransformPoint(g.Center);
                // }

                for (var l = 0; l < Parts[i].Texts.Count; l++)
                {
                    Parts[i].Texts[l].Matrix = m;
                    Parts[i].Texts[l].ReferencePoint = m.TransformPoint(Parts[i].Texts[l].ReferencePoint);
                }
            }

            #endregion

            #region annotations

            foreach (var p in elems.Element("Annotations")?.Elements())
            {
                switch (p.Name.LocalName)
                {
                    case "AnnotationLength":
                        /* var ann = new AnnotationLength
                         {
                             DimensionLineAnnotation =
                                 DimensionLineAnnotation.Read(p.Element("DimensionLineAnnotation")),
                             AnnotationSubType = p.Element("AnnotationSubType").Value,
                             Distance = p.Element("Distance").Value,
                             ElSource1 = ElSource.Read(p.Element("ElSource1")),
                             ElSource2 = ElSource.Read(p.Element("ElSource2")),
                             LineLeftContour = Line.Read(p.Element("LineLeftContour")),
                             LineRightContour = Line.Read(p.Element("LineRightContour"))
                         };
                         Annotations.AnnotationLengths.Add(ann);*/
                        var dim = p.Element("DimensionLineAnnotation");

                        DimensionLineAnnotations.Add(DimensionLineAnnotation.Read(dim));

                        break;
                    default:
                        Console.WriteLine("unknown annotation element: {0}", p.Name.LocalName);
                        break;
                }
            }

            #endregion

            #region texts

            foreach (var node in elems.Element("Texts").Elements())
            {
                if (node.Name.LocalName == "TextProfile")
                {
                    var textProfile = TextProfile.Read(node);
                    Texts.Add(textProfile);
                }
            }

            #endregion

            #region bridges and half-bridges

            foreach (var p in elems.Element("Bridges").Elements())
            {
                switch (p.Name.LocalName)
                {
                    case "Bridge":
                        var b = Bridge.Read(p);
                        Bridges.Add(b);
                        break;


                    case "HalfBridge":
                        var hb = Bridge.Read(p);
                        Bridges.Add(hb);
                        break;

                    default:
                        Console.WriteLine("unknown bridges element: {0}", p.Name.LocalName);
                        break;
                }
            }

            #endregion

            BridgesCount = Bridges.Count;

            #region text symbols count

            TextSymbolsCount = (from op in originalParts
                from tp in op.Texts
                select tp.Text.Replace(" ", "")
                into stripped
                select stripped.Length).Sum();

            #endregion
        }

        public Rectangle GetBBox()
        {
            double maxHeight = double.MinValue,
                maxWidth = double.MinValue,
                minHeight = double.MaxValue,
                minWidth = double.MaxValue;

            foreach (var g in Plate.Profiles.SelectMany(p => p.Geometry))
            {
                if (g.Start.X > maxWidth)
                {
                    maxWidth = g.Start.X;
                }

                if (g.Start.X < minWidth)
                {
                    minWidth = g.Start.X;
                }

                if (g.Start.Y > maxHeight)
                {
                    maxHeight = g.Start.Y;
                }

                if (g.Start.Y < minHeight)
                {
                    minHeight = g.Start.Y;
                }

                if (g.End.X > maxWidth)
                {
                    maxWidth = g.End.X;
                }

                if (g.End.X < minWidth)
                {
                    minWidth = g.End.X;
                }

                if (g.End.Y > maxHeight)
                {
                    maxHeight = g.End.Y;
                }

                if (g.End.Y < minHeight)
                {
                    minHeight = g.End.Y;
                }
            }

            foreach (var g in Remnants.SelectMany(remnant => remnant.Profiles.SelectMany(p => p.Geometry)))
            {
                if (g.Start.X > maxWidth)
                {
                    maxWidth = g.Start.X;
                }

                if (g.Start.X < minWidth)
                {
                    minWidth = g.Start.X;
                }

                if (g.Start.Y > maxHeight)
                {
                    maxHeight = g.Start.Y;
                }

                if (g.Start.Y < minHeight)
                {
                    minHeight = g.Start.Y;
                }

                if (g.End.X > maxWidth)
                {
                    maxWidth = g.End.X;
                }

                if (g.End.X < minWidth)
                {
                    minWidth = g.End.X;
                }

                if (g.End.Y > maxHeight)
                {
                    maxHeight = g.End.Y;
                }

                if (g.End.Y < minHeight)
                {
                    minHeight = g.End.Y;
                }
            }

            foreach (var g in OriginalRemnants.SelectMany(remnant => remnant.Profiles.SelectMany(p => p.Geometry)))
            {
                if (g.Start.X > maxWidth)
                {
                    maxWidth = g.Start.X;
                }

                if (g.Start.X < minWidth)
                {
                    minWidth = g.Start.X;
                }

                if (g.Start.Y > maxHeight)
                {
                    maxHeight = g.Start.Y;
                }

                if (g.Start.Y < minHeight)
                {
                    minHeight = g.Start.Y;
                }

                if (g.End.X > maxWidth)
                {
                    maxWidth = g.End.X;
                }

                if (g.End.X < minWidth)
                {
                    minWidth = g.End.X;
                }

                if (g.End.Y > maxHeight)
                {
                    maxHeight = g.End.Y;
                }

                if (g.End.Y < minHeight)
                {
                    minHeight = g.End.Y;
                }
            }

            foreach (var g in Parts.SelectMany(part => part.Profiles.SelectMany(p => p.Geometry)))
            {
                if (g.Start.X > maxWidth)
                {
                    maxWidth = g.Start.X;
                }

                if (g.Start.X < minWidth)
                {
                    minWidth = g.Start.X;
                }

                if (g.Start.Y > maxHeight)
                {
                    maxHeight = g.Start.Y;
                }

                if (g.Start.Y < minHeight)
                {
                    minHeight = g.Start.Y;
                }

                if (g.End.X > maxWidth)
                {
                    maxWidth = g.End.X;
                }

                if (g.End.X < minWidth)
                {
                    minWidth = g.End.X;
                }

                if (g.End.Y > maxHeight)
                {
                    maxHeight = g.End.Y;
                }

                if (g.End.Y < minHeight)
                {
                    minHeight = g.End.Y;
                }
            }

            foreach (var t in Texts)
            {
                if (t.ReferencePoint.X > maxWidth)
                {
                    maxWidth = t.ReferencePoint.X;
                }

                if (t.ReferencePoint.X < minWidth)
                {
                    minWidth = t.ReferencePoint.X;
                }

                if (t.ReferencePoint.Y > maxHeight)
                {
                    maxHeight = t.ReferencePoint.Y;
                }

                if (t.ReferencePoint.Y < minHeight)
                {
                    minHeight = t.ReferencePoint.Y;
                }
            }

            var x = (int)Math.Ceiling(minWidth);
            var y = (int)Math.Ceiling(minHeight);
            var width = (int)Math.Ceiling(maxWidth - minWidth);
            var height = (int)Math.Ceiling(maxHeight - minHeight);

            return new Rectangle(x, y, width, height);
        }
    }
}