using netDxf;
using netDxf.Entities;

internal sealed class Program {
    internal static void Main() {
        var doc = new DxfDocument();
        doc.Entities.Add(new Line(new Vector2(0,0), new Vector2(100,100)));
        doc.Entities.Add(new Circle(new Vector2(50,50), 20));
        doc.Save("..\\samples\\sample.dxf");
        System.Console.WriteLine("DXF generated successfully!");
    }
}
