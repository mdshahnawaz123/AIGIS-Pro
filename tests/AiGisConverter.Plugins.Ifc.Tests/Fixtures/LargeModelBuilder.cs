using System.Globalization;
using System.Text;

namespace AiGisConverter.Plugins.Ifc.Tests.Fixtures;

/// <summary>
/// Generates schema-valid IFC4 models of arbitrary size for performance and stress testing.
/// </summary>
/// <remarks>
/// <para>
/// Written rather than checked in: a hundred thousand element IFC file is tens of megabytes, which
/// has no place in a repository. Generating it makes the size a parameter, so the same test can
/// prove scaling behaviour by reading two sizes and comparing.
/// </para>
/// <para>
/// The shape matters as much as the size. xBIM resolves the inverse attributes the reader walks —
/// <c>IsTypedBy</c>, <c>HasAssociations</c>, <c>ContainedInStructure</c> — by scanning relationship
/// instances and testing whether their related-objects set contains the element. Cost per element
/// is therefore proportional to the size of that set, so a model where one relationship covers
/// every element is quadratic no matter how efficient the reader is.
/// </para>
/// <para>
/// Real exporters do not produce that shape. Measured against a production Revit IFC2X3 export of
/// roughly four thousand three hundred products: <c>IfcRelDefinesByType</c> sets have a median of 1
/// and a mean of 18, <c>IfcRelAssociatesMaterial</c> a mean of 3.5, and spatial containment a mean
/// of 249 per storey. Set sizes stay bounded as the model grows — the exporter emits more
/// relationships, not fatter ones. <see cref="BuildRealistic"/> reproduces that, which is what makes
/// a timing measurement taken against it meaningful.
/// </para>
/// <para>
/// <see cref="BuildHighFanIn"/> keeps the opposite shape on purpose, for the correctness question
/// "does one shared relationship reach every element it names". That is worth testing and is used
/// only at a small size, where the quadratic term is irrelevant.
/// </para>
/// </remarks>
internal static class LargeModelBuilder
{
    /// <summary>Elements sharing one spatial containment relationship. Production mean was 249.</summary>
    private const int ElementsPerStorey = 250;

    /// <summary>Elements sharing one type object. Production mean was 18.</summary>
    private const int ElementsPerType = 20;

    /// <summary>Elements sharing one material association. Production mean was 3.5.</summary>
    private const int ElementsPerMaterial = 4;

    /// <summary>How many elements the single classification association covers.</summary>
    /// <remarks>Production had three classification relationships with a mean of 169 elements.</remarks>
    private const int ClassifiedElements = 200;

    /// <summary>
    /// Builds a model whose relationship set sizes match a production exporter.
    /// </summary>
    /// <remarks>
    /// Every relationship covers a bounded number of elements, so growing the model adds
    /// relationships rather than enlarging existing ones. This is the shape timing tests must use.
    /// </remarks>
    /// <param name="elementCount">How many building elements to emit.</param>
    /// <returns>The IFC document as text.</returns>
    internal static string BuildRealistic(int elementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementCount);

        return Build(
            elementCount,
            StoreyCountFor(elementCount),
            ElementsPerType,
            ElementsPerMaterial,
            Math.Min(ClassifiedElements, elementCount));
    }

    /// <summary>
    /// Builds a model where one type, material and classification cover every element.
    /// </summary>
    /// <remarks>
    /// Deliberately the worst case for inverse resolution. Use it to prove a shared relationship
    /// fans in to every element it names — never to measure time, because its cost is quadratic in
    /// the element count by construction rather than by any fault of the reader.
    /// </remarks>
    /// <param name="elementCount">How many building elements to emit. Keep this small.</param>
    /// <returns>The IFC document as text.</returns>
    internal static string BuildHighFanIn(int elementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementCount);

        return Build(elementCount, storeyCount: 1, elementCount, elementCount, elementCount);
    }

    /// <summary>
    /// Builds a model with an explicit storey count, for spatial-structure tests.
    /// </summary>
    /// <param name="elementCount">How many building elements to emit.</param>
    /// <param name="storeyCount">How many storeys to spread them across.</param>
    /// <returns>The IFC document as text.</returns>
    internal static string BuildWithStoreys(int elementCount, int storeyCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(storeyCount);

        return Build(
            elementCount,
            storeyCount,
            ElementsPerType,
            ElementsPerMaterial,
            Math.Min(ClassifiedElements, elementCount));
    }

    /// <summary>Gets the storey count that keeps elements-per-storey at the production mean.</summary>
    /// <param name="elementCount">How many building elements the model holds.</param>
    /// <returns>The number of storeys to spread them across, at least one.</returns>
    internal static int StoreyCountFor(int elementCount) =>
        Math.Max(1, (int)Math.Ceiling(elementCount / (double)ElementsPerStorey));

    private static string Build(
        int elementCount,
        int storeyCount,
        int elementsPerType,
        int elementsPerMaterial,
        int classifiedElements)
    {
        StringBuilder ifc = new(elementCount * 260);
        int id = 1;

        int Next() => id++;

        ifc.AppendLine("ISO-10303-21;");
        ifc.AppendLine("HEADER;");
        ifc.AppendLine("FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');");
        ifc.AppendLine("FILE_NAME('large.ifc','2026-08-04T00:00:00',(''),(''),'AiGis','','');");
        ifc.AppendLine("FILE_SCHEMA(('IFC4'));");
        ifc.AppendLine("ENDSEC;");
        ifc.AppendLine("DATA;");

        int person = Next(), org = Next(), personAndOrg = Next(), application = Next(), owner = Next();
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{person}=IFCPERSON($,'T',$,$,$,$,$,$);");
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{org}=IFCORGANIZATION($,'AiGis',$,$,$);");
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{personAndOrg}=IFCPERSONANDORGANIZATION(#{person},#{org},$);");
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{application}=IFCAPPLICATION(#{org},'1.0','AiGis','AIGIS');");
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{owner}=IFCOWNERHISTORY(#{personAndOrg},#{application},$,.ADDED.,$,$,$,0);");

        int origin = Next(), axis = Next(), context = Next(), metre = Next(), units = Next();
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{origin}=IFCCARTESIANPOINT((0.,0.,0.));");
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{axis}=IFCAXIS2PLACEMENT3D(#{origin},$,$);");
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{context}=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.0E-5,#{axis},$);");
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{metre}=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);");
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{units}=IFCUNITASSIGNMENT((#{metre}));");

        int project = Next();
        ifc.AppendLine(CultureInfo.InvariantCulture,
            $"#{project}=IFCPROJECT('{MakeGuid(project)}',#{owner},'Large',$,$,$,$,(#{context}),#{units});");

        int sitePlacement = Next(), site = Next();
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{sitePlacement}=IFCLOCALPLACEMENT($,#{axis});");
        ifc.AppendLine(CultureInfo.InvariantCulture,
            $"#{site}=IFCSITE('{MakeGuid(site)}',#{owner},'Site',$,$,#{sitePlacement},$,$,.ELEMENT.,$,$,$,$,$);");

        int buildingPlacement = Next(), building = Next();
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{buildingPlacement}=IFCLOCALPLACEMENT(#{sitePlacement},#{axis});");
        ifc.AppendLine(CultureInfo.InvariantCulture,
            $"#{building}=IFCBUILDING('{MakeGuid(building)}',#{owner},'Building',$,$,#{buildingPlacement},$,$,.ELEMENT.,$,$,$);");

        int aggregateSite = Next(), aggregateBuilding = Next();
        ifc.AppendLine(CultureInfo.InvariantCulture,
            $"#{aggregateSite}=IFCRELAGGREGATES('{MakeGuid(aggregateSite)}',#{owner},$,$,#{project},(#{site}));");
        ifc.AppendLine(CultureInfo.InvariantCulture,
            $"#{aggregateBuilding}=IFCRELAGGREGATES('{MakeGuid(aggregateBuilding)}',#{owner},$,$,#{site},(#{building}));");

        List<int> storeys = [];
        List<int> storeyPlacements = [];

        for (int s = 0; s < storeyCount; s++)
        {
            int point = Next(), placementAxis = Next(), placement = Next(), storey = Next();
            string elevation = (s * 4).ToString(CultureInfo.InvariantCulture);

            ifc.AppendLine(CultureInfo.InvariantCulture, $"#{point}=IFCCARTESIANPOINT((0.,0.,{elevation}.));");
            ifc.AppendLine(CultureInfo.InvariantCulture, $"#{placementAxis}=IFCAXIS2PLACEMENT3D(#{point},$,$);");
            ifc.AppendLine(CultureInfo.InvariantCulture, $"#{placement}=IFCLOCALPLACEMENT(#{buildingPlacement},#{placementAxis});");
            ifc.AppendLine(CultureInfo.InvariantCulture,
                $"#{storey}=IFCBUILDINGSTOREY('{MakeGuid(storey)}',#{owner},'Level {s + 1}',$,$,#{placement},$,$,.ELEMENT.,{elevation}.);");

            storeys.Add(storey);
            storeyPlacements.Add(placement);
        }

        int aggregateStoreys = Next();
        ifc.AppendLine(CultureInfo.InvariantCulture,
            $"#{aggregateStoreys}=IFCRELAGGREGATES('{MakeGuid(aggregateStoreys)}',#{owner},$,$,#{building},({Refs(storeys)}));");

        int classification = Next(), classificationReference = Next();
        ifc.AppendLine(CultureInfo.InvariantCulture, $"#{classification}=IFCCLASSIFICATION('BSI','2015',$,'Uniclass 2015',$,$,$);");
        ifc.AppendLine(CultureInfo.InvariantCulture,
            $"#{classificationReference}=IFCCLASSIFICATIONREFERENCE($,'EF_25_10','Walls',#{classification},$,$);");

        List<List<int>> byStorey = [.. Enumerable.Range(0, storeyCount).Select(static _ => new List<int>())];
        List<int> allElements = [];

        for (int e = 0; e < elementCount; e++)
        {
            int storeyIndex = e % storeyCount;
            int point = Next(), placementAxis = Next(), placement = Next(), wall = Next();

            ifc.AppendLine(CultureInfo.InvariantCulture,
                $"#{point}=IFCCARTESIANPOINT(({e % 100}.,{e / 100}.,0.));");
            ifc.AppendLine(CultureInfo.InvariantCulture, $"#{placementAxis}=IFCAXIS2PLACEMENT3D(#{point},$,$);");
            ifc.AppendLine(CultureInfo.InvariantCulture,
                $"#{placement}=IFCLOCALPLACEMENT(#{storeyPlacements[storeyIndex]},#{placementAxis});");
            ifc.AppendLine(CultureInfo.InvariantCulture,
                $"#{wall}=IFCWALL('{MakeGuid(wall)}',#{owner},'Wall {e}',$,$,#{placement},$,$,.SOLIDWALL.);");

            byStorey[storeyIndex].Add(wall);
            allElements.Add(wall);
        }

        for (int s = 0; s < storeyCount; s++)
        {
            if (byStorey[s].Count == 0)
            {
                continue;
            }

            int contained = Next();

            ifc.AppendLine(CultureInfo.InvariantCulture,
                $"#{contained}=IFCRELCONTAINEDINSPATIALSTRUCTURE('{MakeGuid(contained)}',#{owner},$,$,({Refs(byStorey[s])}),#{storeys[s]});");
        }

        // One type object per batch. The property set hangs off the type, so every element in the
        // batch inherits it — the path most of a BIM model's properties actually travel.
        foreach (int[] batch in Batches(allElements, elementsPerType))
        {
            int property = Next(), propertySet = Next(), wallType = Next(), typeRelation = Next();

            ifc.AppendLine(CultureInfo.InvariantCulture, $"#{property}=IFCPROPERTYSINGLEVALUE('Manufacturer',$,IFCLABEL('Acme'),$);");
            ifc.AppendLine(CultureInfo.InvariantCulture,
                $"#{propertySet}=IFCPROPERTYSET('{MakeGuid(propertySet)}',#{owner},'Pset_WallCommon',$,(#{property}));");
            ifc.AppendLine(CultureInfo.InvariantCulture,
                $"#{wallType}=IFCWALLTYPE('{MakeGuid(wallType)}',#{owner},'Standard Wall',$,$,(#{propertySet}),$,$,$,.SOLIDWALL.);");
            ifc.AppendLine(CultureInfo.InvariantCulture,
                $"#{typeRelation}=IFCRELDEFINESBYTYPE('{MakeGuid(typeRelation)}',#{owner},$,$,({Refs(batch)}),#{wallType});");
        }

        foreach (int[] batch in Batches(allElements, elementsPerMaterial))
        {
            int material = Next(), materialRelation = Next();

            ifc.AppendLine(CultureInfo.InvariantCulture, $"#{material}=IFCMATERIAL('Concrete C40',$,$);");
            ifc.AppendLine(CultureInfo.InvariantCulture,
                $"#{materialRelation}=IFCRELASSOCIATESMATERIAL('{MakeGuid(materialRelation)}',#{owner},$,$,({Refs(batch)}),#{material});");
        }

        if (classifiedElements > 0)
        {
            int classified = Next();
            ifc.AppendLine(CultureInfo.InvariantCulture,
                $"#{classified}=IFCRELASSOCIATESCLASSIFICATION('{MakeGuid(classified)}',#{owner},$,$,({Refs(allElements.Take(classifiedElements))}),#{classificationReference});");
        }

        ifc.AppendLine("ENDSEC;");
        ifc.AppendLine("END-ISO-10303-21;");

        return ifc.ToString();
    }

    /// <summary>Splits entity numbers into batches of a bounded size.</summary>
    /// <param name="entities">The entity numbers to split.</param>
    /// <param name="size">The maximum batch size.</param>
    /// <returns>The batches, in order.</returns>
    private static IEnumerable<int[]> Batches(IReadOnlyList<int> entities, int size)
    {
        for (int start = 0; start < entities.Count; start += size)
        {
            yield return [.. entities.Skip(start).Take(size)];
        }
    }

    /// <summary>Formats entity numbers as a STEP reference list.</summary>
    /// <param name="entities">The entity numbers.</param>
    /// <returns>A comma-separated list of <c>#n</c> references.</returns>
    private static string Refs(IEnumerable<int> entities) =>
        string.Join(",", entities.Select(static e => $"#{e.ToString(CultureInfo.InvariantCulture)}"));

    /// <summary>Builds a deterministic 22-character identifier, as IFC requires.</summary>
    /// <remarks>
    /// <c>IfcGloballyUniqueId</c> is <c>STRING(22) FIXED</c>. Deriving it from the entity number
    /// keeps it unique and reproducible, so a failing run can be repeated exactly.
    /// </remarks>
    /// <param name="entity">The entity number to derive from.</param>
    /// <returns>A 22-character identifier.</returns>
    private static string MakeGuid(int entity)
    {
        string suffix = entity.ToString(CultureInfo.InvariantCulture);

        return "G" + new string('0', 21 - suffix.Length) + suffix;
    }
}
