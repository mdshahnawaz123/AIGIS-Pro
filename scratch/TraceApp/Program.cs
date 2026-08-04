#pragma warning disable CS1591, CA1050
using System;
using NetTopologySuite.Geometries;
using NetTopologySuite.Precision;
public class Program { public static void Main() { var factory = new GeometryFactory(); var line = factory.CreateLineString(new Coordinate[] { new Coordinate(528000, 1200000), new Coordinate(528000.0000001, 1200000.0000001) }); var pm = new PrecisionModel(10000000); var reducer = new GeometryPrecisionReducer(pm) { ChangePrecisionModel = true }; var reduced = reducer.Reduce(line); Console.WriteLine("Original: " + line.IsEmpty + ", Reduced: " + reduced.IsEmpty); } }
