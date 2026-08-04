namespace AiGisConverter.Plugins.Ifc.Tests.Fixtures;

/// <summary>
/// Schema-valid IFC documents used by the verification tests.
/// </summary>
/// <remarks>
/// Hand-authored rather than exported, so every relationship under test is present and explicit:
/// the spatial tree, a door filling an opening that voids a wall, property sets and quantities.
/// Real exporter output is layered on top of these by <c>RealModelTests</c> when files are supplied.
/// </remarks>
internal static class IfcSamples
{
    // IfcGloballyUniqueId is STRING(22) FIXED. These are declared once so a test can never assert
    // an id that has drifted from the document, and so the 22-character rule stays visible.
    internal const string SiteId = "0SITE00000000000000001";
    internal const string BuildingId = "0BUILDING0000000000001";
    internal const string StoreyId = "0STOREY000000000000001";
    internal const string SpaceId = "0SPACE0000000000000001";
    internal const string WallId = "0WALL00000000000000001";
    internal const string SlabId = "0SLAB00000000000000001";
    internal const string ColumnId = "0COLUMN000000000000001";
    internal const string DoorId = "0DOOR00000000000000001";
    internal const string OpeningId = "0OPENING00000000000001";

    internal const string LegacySiteId = "1SITE00000000000000001";
    internal const string LegacyBuildingId = "1BUILDING0000000000001";
    internal const string LegacyStoreyId = "1STOREY000000000000001";
    internal const string LegacyWallId = "1WALL00000000000000001";

    /// <summary>An IFC4 building: site, building, storey, space, wall, slab, column and a hosted door.</summary>
    internal const string Ifc4Building = """
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');
        FILE_NAME('verification.ifc','2026-08-04T00:00:00',(''),(''),'AiGisConverter','','');
        FILE_SCHEMA(('IFC4'));
        ENDSEC;
        DATA;
        #1=IFCPERSON($,'Tester',$,$,$,$,$,$);
        #2=IFCORGANIZATION($,'AiGis',$,$,$);
        #3=IFCPERSONANDORGANIZATION(#1,#2,$);
        #4=IFCAPPLICATION(#2,'1.0','AiGisConverter','AIGIS');
        #5=IFCOWNERHISTORY(#3,#4,$,.ADDED.,$,$,$,0);
        #6=IFCCARTESIANPOINT((0.,0.,0.));
        #7=IFCAXIS2PLACEMENT3D(#6,$,$);
        #8=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.0E-5,#7,$);
        #9=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);
        #10=IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.);
        #11=IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.);
        #12=IFCUNITASSIGNMENT((#9,#10,#11));
        #13=IFCPROJECT('0PROJECT00000000000001',#5,'Verification Project',$,$,$,$,(#8),#12);
        #20=IFCLOCALPLACEMENT($,#7);
        #21=IFCSITE('0SITE00000000000000001',#5,'Site A',$,$,#20,$,$,.ELEMENT.,$,$,$,$,$);
        #22=IFCCARTESIANPOINT((0.,0.,0.));
        #23=IFCAXIS2PLACEMENT3D(#22,$,$);
        #24=IFCLOCALPLACEMENT(#20,#23);
        #25=IFCBUILDING('0BUILDING0000000000001',#5,'Building A',$,$,#24,$,$,.ELEMENT.,$,$,$);
        #26=IFCCARTESIANPOINT((0.,0.,3.));
        #27=IFCAXIS2PLACEMENT3D(#26,$,$);
        #28=IFCLOCALPLACEMENT(#24,#27);
        #29=IFCBUILDINGSTOREY('0STOREY000000000000001',#5,'Level 1',$,$,#28,$,'Ground Floor',.ELEMENT.,3.0);
        #30=IFCRELAGGREGATES('0AGG100000000000000001',#5,$,$,#13,(#21));
        #31=IFCRELAGGREGATES('0AGG200000000000000001',#5,$,$,#21,(#25));
        #32=IFCRELAGGREGATES('0AGG300000000000000001',#5,$,$,#25,(#29));
        #40=IFCCARTESIANPOINT((10.,20.,0.));
        #41=IFCAXIS2PLACEMENT3D(#40,$,$);
        #42=IFCLOCALPLACEMENT(#28,#41);
        #43=IFCWALL('0WALL00000000000000001',#5,'Basic Wall',$,'Concrete 200',#42,$,$,.SOLIDWALL.);
        #50=IFCCARTESIANPOINT((0.,0.,0.));
        #51=IFCAXIS2PLACEMENT3D(#50,$,$);
        #52=IFCLOCALPLACEMENT(#28,#51);
        #53=IFCSLAB('0SLAB00000000000000001',#5,'Floor Slab',$,'RC 250',#52,$,$,.FLOOR.);
        #60=IFCCARTESIANPOINT((5.,5.,0.));
        #61=IFCAXIS2PLACEMENT3D(#60,$,$);
        #62=IFCLOCALPLACEMENT(#28,#61);
        #63=IFCCOLUMN('0COLUMN000000000000001',#5,'Column C1',$,'RC 400x400',#62,$,$,.COLUMN.);
        #70=IFCCARTESIANPOINT((11.,20.,0.));
        #71=IFCAXIS2PLACEMENT3D(#70,$,$);
        #72=IFCLOCALPLACEMENT(#42,#71);
        #73=IFCOPENINGELEMENT('0OPENING00000000000001',#5,'Door Opening',$,$,#72,$,$,.OPENING.);
        #74=IFCRELVOIDSELEMENT('0VOIDS0000000000000001',#5,$,$,#43,#73);
        #75=IFCCARTESIANPOINT((11.,20.,0.));
        #76=IFCAXIS2PLACEMENT3D(#75,$,$);
        #77=IFCLOCALPLACEMENT(#72,#76);
        #78=IFCDOOR('0DOOR00000000000000001',#5,'M_Single-Flush',$,'900x2100',#77,$,$,2.1,0.9,.DOOR.,.SINGLE_SWING_LEFT.,$);
        #79=IFCRELFILLSELEMENT('0FILLS0000000000000001',#5,$,$,#73,#78);
        #80=IFCCARTESIANPOINT((2.,2.,0.));
        #81=IFCAXIS2PLACEMENT3D(#80,$,$);
        #82=IFCLOCALPLACEMENT(#28,#81);
        #83=IFCSPACE('0SPACE0000000000000001',#5,'Office 101',$,$,#82,$,'Open Office',.ELEMENT.,.INTERNAL.,3.0);
        #90=IFCRELCONTAINEDINSPATIALSTRUCTURE('0CONTAIN00000000000001',#5,$,$,(#43,#53,#63,#78),#29);
        #91=IFCRELAGGREGATES('0AGG400000000000000001',#5,$,$,#29,(#83));
        #100=IFCPROPERTYSINGLEVALUE('FireRating',$,IFCLABEL('REI60'),$);
        #101=IFCPROPERTYSINGLEVALUE('IsExternal',$,IFCBOOLEAN(.T.),$);
        #102=IFCPROPERTYSINGLEVALUE('LoadBearing',$,IFCBOOLEAN(.T.),$);
        #103=IFCPROPERTYSET('0PSET00000000000000001',#5,'Pset_WallCommon',$,(#100,#101,#102));
        #104=IFCRELDEFINESBYPROPERTIES('0RELDEF000000000000001',#5,$,$,(#43),#103);
        #110=IFCQUANTITYAREA('NetSideArea',$,$,15.5,$);
        #111=IFCQUANTITYLENGTH('Length',$,$,5.0,$);
        #112=IFCQUANTITYVOLUME('NetVolume',$,$,3.1,$);
        #113=IFCELEMENTQUANTITY('0QTO000000000000000001',#5,'Qto_WallBaseQuantities',$,$,(#110,#111,#112));
        #114=IFCRELDEFINESBYPROPERTIES('0RELDEF000000000000002',#5,$,$,(#43),#113);
        ENDSEC;
        END-ISO-10303-21;
        """;

    /// <summary>A campus: two buildings, one with two storeys, plus materials on the elements.</summary>
    /// <remarks>
    /// Exists to prove the hierarchy is a real tree rather than a single assumed path. A reader
    /// that hard-codes "the storey" or "the building" passes the single-building fixture and fails
    /// here, which is exactly the case a production model presents.
    /// </remarks>
    internal const string Ifc4Campus = """
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');
        FILE_NAME('campus.ifc','2026-08-04T00:00:00',(''),(''),'AiGisConverter','','');
        FILE_SCHEMA(('IFC4'));
        ENDSEC;
        DATA;
        #1=IFCPERSON($,'Tester',$,$,$,$,$,$);
        #2=IFCORGANIZATION($,'AiGis',$,$,$);
        #3=IFCPERSONANDORGANIZATION(#1,#2,$);
        #4=IFCAPPLICATION(#2,'1.0','AiGisConverter','AIGIS');
        #5=IFCOWNERHISTORY(#3,#4,$,.ADDED.,$,$,$,0);
        #6=IFCCARTESIANPOINT((0.,0.,0.));
        #7=IFCAXIS2PLACEMENT3D(#6,$,$);
        #8=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.0E-5,#7,$);
        #9=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);
        #10=IFCUNITASSIGNMENT((#9));
        #11=IFCPROJECT('2PROJECT00000000000001',#5,'Campus',$,$,$,$,(#8),#10);
        #20=IFCLOCALPLACEMENT($,#7);
        #21=IFCSITE('2SITE00000000000000001',#5,'Campus Site',$,$,#20,$,$,.ELEMENT.,$,$,$,$,$);
        #22=IFCLOCALPLACEMENT(#20,#7);
        #23=IFCBUILDING('2BLDGA0000000000000001',#5,'Block A',$,$,#22,$,$,.ELEMENT.,$,$,$);
        #24=IFCCARTESIANPOINT((500.,0.,0.));
        #25=IFCAXIS2PLACEMENT3D(#24,$,$);
        #26=IFCLOCALPLACEMENT(#20,#25);
        #27=IFCBUILDING('2BLDGB0000000000000001',#5,'Block B',$,$,#26,$,$,.ELEMENT.,$,$,$);
        #30=IFCCARTESIANPOINT((0.,0.,0.));
        #31=IFCAXIS2PLACEMENT3D(#30,$,$);
        #32=IFCLOCALPLACEMENT(#22,#31);
        #33=IFCBUILDINGSTOREY('2A0L100000000000000001',#5,'A-Level 1',$,$,#32,$,$,.ELEMENT.,0.0);
        #34=IFCCARTESIANPOINT((0.,0.,4.));
        #35=IFCAXIS2PLACEMENT3D(#34,$,$);
        #36=IFCLOCALPLACEMENT(#22,#35);
        #37=IFCBUILDINGSTOREY('2A0L200000000000000001',#5,'A-Level 2',$,$,#36,$,$,.ELEMENT.,4.0);
        #38=IFCCARTESIANPOINT((0.,0.,0.));
        #39=IFCAXIS2PLACEMENT3D(#38,$,$);
        #40=IFCLOCALPLACEMENT(#26,#39);
        #41=IFCBUILDINGSTOREY('2B0L100000000000000001',#5,'B-Level 1',$,$,#40,$,$,.ELEMENT.,0.0);
        #50=IFCRELAGGREGATES('2AGG100000000000000001',#5,$,$,#11,(#21));
        #51=IFCRELAGGREGATES('2AGG200000000000000001',#5,$,$,#21,(#23,#27));
        #52=IFCRELAGGREGATES('2AGG300000000000000001',#5,$,$,#23,(#33,#37));
        #53=IFCRELAGGREGATES('2AGG400000000000000001',#5,$,$,#27,(#41));
        #60=IFCCARTESIANPOINT((1.,1.,0.));
        #61=IFCAXIS2PLACEMENT3D(#60,$,$);
        #62=IFCLOCALPLACEMENT(#32,#61);
        #63=IFCWALL('2A1WALL000000000000001',#5,'A1 Wall',$,$,#62,$,$,.SOLIDWALL.);
        #64=IFCCARTESIANPOINT((2.,2.,0.));
        #65=IFCAXIS2PLACEMENT3D(#64,$,$);
        #66=IFCLOCALPLACEMENT(#36,#65);
        #67=IFCWALL('2A2WALL000000000000001',#5,'A2 Wall',$,$,#66,$,$,.SOLIDWALL.);
        #68=IFCCARTESIANPOINT((3.,3.,0.));
        #69=IFCAXIS2PLACEMENT3D(#68,$,$);
        #70=IFCLOCALPLACEMENT(#40,#69);
        #71=IFCWALL('2B1WALL000000000000001',#5,'B1 Wall',$,$,#70,$,$,.SOLIDWALL.);
        #80=IFCRELCONTAINEDINSPATIALSTRUCTURE('2CON100000000000000001',#5,$,$,(#63),#33);
        #81=IFCRELCONTAINEDINSPATIALSTRUCTURE('2CON200000000000000001',#5,$,$,(#67),#37);
        #82=IFCRELCONTAINEDINSPATIALSTRUCTURE('2CON300000000000000001',#5,$,$,(#71),#41);
        #90=IFCMATERIAL('Concrete C40',$,$);
        #91=IFCRELASSOCIATESMATERIAL('2MAT100000000000000001',#5,$,$,(#63,#67),#90);
        #92=IFCMATERIAL('Brick',$,$);
        #93=IFCRELASSOCIATESMATERIAL('2MAT200000000000000001',#5,$,$,(#71),#92);
        #100=IFCCARTESIANPOINT((5.,5.,0.));
        #101=IFCAXIS2PLACEMENT3D(#100,$,$);
        #102=IFCLOCALPLACEMENT(#32,#101);
        #103=IFCSPACE('2A1SPACE00000000000001',#5,'A1 Room',$,$,#102,$,'Meeting Room',.ELEMENT.,.INTERNAL.,0.0);
        #104=IFCRELAGGREGATES('2AGG500000000000000001',#5,$,$,#33,(#103));
        ENDSEC;
        END-ISO-10303-21;
        """;

    /// <summary>A model exercising type objects, inherited and nested property sets,
    /// classification references, quantity sets and a full unit assignment.</summary>
    /// <remarks>
    /// Most of a BIM model's data sits on the type rather than the occurrence, and inside nested
    /// property sets. A reader that only walks the occurrence's own flat sets appears to work and
    /// silently drops the majority of the information, which is what this fixture is here to catch.
    /// </remarks>
    internal const string Ifc4TypesAndProperties = """
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');
        FILE_NAME('types.ifc','2026-08-04T00:00:00',(''),(''),'AiGisConverter','','');
        FILE_SCHEMA(('IFC4'));
        ENDSEC;
        DATA;
        #1=IFCPERSON($,'Tester',$,$,$,$,$,$);
        #2=IFCORGANIZATION($,'AiGis',$,$,$);
        #3=IFCPERSONANDORGANIZATION(#1,#2,$);
        #4=IFCAPPLICATION(#2,'1.0','AiGisConverter','AIGIS');
        #5=IFCOWNERHISTORY(#3,#4,$,.ADDED.,$,$,$,0);
        #6=IFCCARTESIANPOINT((0.,0.,0.));
        #7=IFCAXIS2PLACEMENT3D(#6,$,$);
        #8=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.0E-5,#7,$);
        #9=IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.);
        #10=IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.);
        #11=IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.);
        #12=IFCSIUNIT(*,.PLANEANGLEUNIT.,$,.RADIAN.);
        #13=IFCUNITASSIGNMENT((#9,#10,#11,#12));
        #14=IFCPROJECT('3PROJECT00000000000001',#5,'Types Project',$,$,$,$,(#8),#13);
        #20=IFCLOCALPLACEMENT($,#7);
        #21=IFCSITE('3SITE00000000000000001',#5,'Site',$,$,#20,$,$,.ELEMENT.,$,$,$,$,$);
        #22=IFCLOCALPLACEMENT(#20,#7);
        #23=IFCBUILDING('3BUILDING0000000000001',#5,'Building',$,$,#22,$,$,.ELEMENT.,$,$,$);
        #24=IFCCARTESIANPOINT((0.,0.,0.));
        #25=IFCAXIS2PLACEMENT3D(#24,$,$);
        #26=IFCLOCALPLACEMENT(#22,#25);
        #27=IFCBUILDINGSTOREY('3STOREY000000000000001',#5,'Level 1',$,$,#26,$,$,.ELEMENT.,0.0);
        #28=IFCRELAGGREGATES('3AGG100000000000000001',#5,$,$,#14,(#21));
        #29=IFCRELAGGREGATES('3AGG200000000000000001',#5,$,$,#21,(#23));
        #30=IFCRELAGGREGATES('3AGG300000000000000001',#5,$,$,#23,(#27));
        #40=IFCPROPERTYSINGLEVALUE('Manufacturer',$,IFCLABEL('Acme'),$);
        #41=IFCPROPERTYSINGLEVALUE('AcousticRating',$,IFCLABEL('45dB'),$);
        #42=IFCPROPERTYSET('3TPSET0000000000000001',#5,'Pset_DoorCommon',$,(#40,#41));
        #43=IFCDOORTYPE('3DOORTYPE0000000000001',#5,'Single Flush 900',$,$,(#42),$,$,$,.DOOR.,.SINGLE_SWING_LEFT.,$,$);
        #50=IFCCARTESIANPOINT((3.,4.,0.));
        #51=IFCAXIS2PLACEMENT3D(#50,$,$);
        #52=IFCLOCALPLACEMENT(#26,#51);
        #53=IFCDOOR('3DOOR00000000000000001',#5,'Door 01',$,$,#52,$,$,2.1,0.9,.DOOR.,.SINGLE_SWING_LEFT.,$);
        #54=IFCRELDEFINESBYTYPE('3RELTYPE00000000000001',#5,$,$,(#53),#43);
        #60=IFCPROPERTYSINGLEVALUE('Status',$,IFCLABEL('New'),$);
        #61=IFCPROPERTYSINGLEVALUE('NestedDepth',$,IFCLENGTHMEASURE(120.),$);
        #62=IFCCOMPLEXPROPERTY('Frame',$,'FrameGroup',(#60,#61));
        #63=IFCPROPERTYSINGLEVALUE('FireRating',$,IFCLABEL('EI30'),$);
        #64=IFCPROPERTYSET('3IPSET0000000000000001',#5,'Pset_DoorInstance',$,(#62,#63));
        #65=IFCRELDEFINESBYPROPERTIES('3RELDEF000000000000001',#5,$,$,(#53),#64);
        #70=IFCQUANTITYAREA('NetArea',$,$,1.89,$);
        #71=IFCQUANTITYLENGTH('Perimeter',$,$,6.0,$);
        #72=IFCELEMENTQUANTITY('3QTO000000000000000001',#5,'Qto_DoorBaseQuantities',$,$,(#70,#71));
        #73=IFCRELDEFINESBYPROPERTIES('3RELDEF000000000000002',#5,$,$,(#53),#72);
        #80=IFCCLASSIFICATION('BSI','2015',$,'Uniclass 2015',$,$,$);
        #81=IFCCLASSIFICATIONREFERENCE($,'EF_25_10','Doors',#80,$,$);
        #82=IFCRELASSOCIATESCLASSIFICATION('3RELCLS000000000000001',#5,$,$,(#53),#81);
        #90=IFCRELCONTAINEDINSPATIALSTRUCTURE('3CON100000000000000001',#5,$,$,(#53),#27);
        ENDSEC;
        END-ISO-10303-21;
        """;

    /// <summary>An IFC2x3 document, to prove the schema-neutral read path handles the older schema.</summary>
    internal const string Ifc2X3Building = """
        ISO-10303-21;
        HEADER;
        FILE_DESCRIPTION(('ViewDefinition [CoordinationView]'),'2;1');
        FILE_NAME('legacy.ifc','2026-08-04T00:00:00',(''),(''),'AiGisConverter','','');
        FILE_SCHEMA(('IFC2X3'));
        ENDSEC;
        DATA;
        #1=IFCPERSON($,'Tester',$,$,$,$,$,$);
        #2=IFCORGANIZATION($,'AiGis',$,$,$);
        #3=IFCPERSONANDORGANIZATION(#1,#2,$);
        #4=IFCAPPLICATION(#2,'1.0','AiGisConverter','AIGIS');
        #5=IFCOWNERHISTORY(#3,#4,$,.ADDED.,$,$,$,0);
        #6=IFCCARTESIANPOINT((0.,0.,0.));
        #7=IFCAXIS2PLACEMENT3D(#6,$,$);
        #8=IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,1.0E-5,#7,$);
        #9=IFCSIUNIT(*,.LENGTHUNIT.,$,.METRE.);
        #10=IFCUNITASSIGNMENT((#9));
        #11=IFCPROJECT('1PROJECT00000000000001',#5,'Legacy Project',$,$,$,$,(#8),#10);
        #20=IFCLOCALPLACEMENT($,#7);
        #21=IFCSITE('1SITE00000000000000001',#5,'Legacy Site',$,$,#20,$,$,.ELEMENT.,$,$,$,$,$);
        #22=IFCLOCALPLACEMENT(#20,#7);
        #23=IFCBUILDING('1BUILDING0000000000001',#5,'Legacy Building',$,$,#22,$,$,.ELEMENT.,$,$,$);
        #24=IFCCARTESIANPOINT((0.,0.,0.));
        #25=IFCAXIS2PLACEMENT3D(#24,$,$);
        #26=IFCLOCALPLACEMENT(#22,#25);
        #27=IFCBUILDINGSTOREY('1STOREY000000000000001',#5,'Legacy Level',$,$,#26,$,$,.ELEMENT.,0.0);
        #28=IFCRELAGGREGATES('1AGG100000000000000001',#5,$,$,#11,(#21));
        #29=IFCRELAGGREGATES('1AGG200000000000000001',#5,$,$,#21,(#23));
        #30=IFCRELAGGREGATES('1AGG300000000000000001',#5,$,$,#23,(#27));
        #40=IFCCARTESIANPOINT((100.,200.,0.));
        #41=IFCAXIS2PLACEMENT3D(#40,$,$);
        #42=IFCLOCALPLACEMENT(#26,#41);
        #43=IFCWALLSTANDARDCASE('1WALL00000000000000001',#5,'Legacy Wall',$,'Block 150',#42,$,$);
        #44=IFCCARTESIANPOINT((105.,205.,0.));
        #45=IFCAXIS2PLACEMENT3D(#44,$,$);
        #46=IFCLOCALPLACEMENT(#26,#45);
        #47=IFCBEAM('1BEAM00000000000000001',#5,'Legacy Beam',$,'Steel UB',#46,$,$);
        #50=IFCRELCONTAINEDINSPATIALSTRUCTURE('1CONTAIN00000000000001',#5,$,$,(#43,#47),#27);
        ENDSEC;
        END-ISO-10303-21;
        """;
}
