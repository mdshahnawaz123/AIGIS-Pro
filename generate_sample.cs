using netDxf;
using netDxf.Entities;

class Program {
    static void Main() {
        var doc = new DxfDocument();
        doc.Entities.Add(new Line(new Vector2(0,0), new Vector2(100,100)));
        doc.Entities.Add(new Circle(new Vector2(50,50), 20));
        doc.Save("samples\\sample.dxf");
    }
}
