using System;
using System.IO;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;

namespace CreateSampleDxf
{
    /// <summary>
    /// Program
    /// </summary>
    public sealed class Program
    {
        /// <summary>
        /// Main
        /// </summary>
        /// <param name="args"></param>
        public static void Main(string[] args)
        {
            var doc = new DxfDocument();

            // Layers
            var roadLayer = new Layer("ROAD_CL") { Color = AciColor.Red };
            var bldgLayer = new Layer("BLDG") { Color = AciColor.Blue };
            var treeLayer = new Layer("TREE") { Color = AciColor.Green };
            var utilLayer = new Layer("UTILITY") { Color = AciColor.Cyan };
            var parcelLayer = new Layer("PARCEL") { Color = AciColor.Yellow };
            var textLayer = new Layer("ANNOTATION") { Color = AciColor.Magenta };

            // Roads (Line)
            var road = new Line(new Vector2(0, 0), new Vector2(100, 0)) { Layer = roadLayer };
            doc.Entities.Add(road);

            // Buildings (Closed Polyline)
            var bldg = new Polyline2D(new[]
            {
                new Vector2(10, 10), new Vector2(20, 10), new Vector2(20, 20), new Vector2(10, 20), new Vector2(10, 10)
            }) { Layer = bldgLayer, IsClosed = true };
            doc.Entities.Add(bldg);

            // Trees (Points/Blocks)
            var tree = new Point(new Vector3(50, 50, 0)) { Layer = treeLayer };
            doc.Entities.Add(tree);
            
            // Utilities (Circle)
            var manhole = new Circle(new Vector3(30, 30, 0), 2) { Layer = utilLayer };
            doc.Entities.Add(manhole);

            // Parcels (Closed Polyline)
            var parcel = new Polyline2D(new[]
            {
                new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100), new Vector2(0, 0)
            }) { Layer = parcelLayer, IsClosed = true };
            doc.Entities.Add(parcel);

            // Text
            var text = new Text("Main St", new Vector2(50, -5), 2.5) { Layer = textLayer };
            doc.Entities.Add(text);

            var dir = @"C:\Users\Mohd Shahnawaz\Downloads\Compressed\AiGisConverter\samples";
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "sample.dxf");
            doc.Save(path);
            
            Console.WriteLine($"Generated DXF at {path}");
        }
    }
}
