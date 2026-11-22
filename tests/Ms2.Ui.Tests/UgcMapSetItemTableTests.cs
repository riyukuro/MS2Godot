using System.Xml.Linq;
using Ms2.Formats.Pack;
using Ms2.Ui.Items;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>CUGCMapSetItemTable (<see cref="UgcMapSetItemTable"/>) on a made-up table, and the item tooltip's housing set
/// block FUN_1409c8df0 on the game data.</summary>
public class UgcMapSetItemTableTests
{
    [Test]
    public void RowsNeedEvenListsAndKeepPiecesInIdOrder()
    {
        var t = new UgcMapSetItemTable(XDocument.Parse("<ms2>"
            + @"<UGCMapSetItem group=""1"" itemID=""30,10,20,10"" itemCount=""1,1,1,2"" minCount=""2"" aniIdle=""a,b,c,d"" aniWork=""e,f,g,h"" effect=""x"" />"
            + @"<UGCMapSetItem group=""2"" itemID=""10,20"" itemCount=""1"" aniIdle=""a,b"" aniWork=""e,f"" />"   // short itemCount
            + @"<UGCMapSetItem group=""3"" itemID=""10,20"" itemCount=""1,1"" aniIdle=""a"" aniWork=""e,f"" />"  // short aniIdle
            + @"<UGCMapSetItem group=""4"" itemID=""10,20"" itemCount=""1,1"" aniIdle=""a,b"" aniWork="",e"" />" // short aniWork
            + @"<UGCMapSetItem group=""5"" itemID=""10"" itemCount=""1"" aniIdle=""a"" aniWork=""e"" />"
            + @"<UGCMapSetItem group=""5"" itemID=""40"" itemCount=""3"" aniIdle=""a"" aniWork=""e"" />"         // replaces group 5
            + @"<UGCMapSetItem itemID="""" itemCount="""" aniIdle="""" aniWork="""" />"                          // group 0
            + "</ms2>"));
        var s = t.Get(1);
        Assert.That(s.MinCount, Is.EqualTo(2u));
        Assert.That(s.Pieces.Keys, Is.EqualTo(new uint[] { 10, 20, 30 }));
        Assert.That(s.Pieces[10], Is.EqualTo(new UgcMapSetItemTable.Piece(1, "b", "f", "", "")));     // the first 10 is kept
        Assert.That(s.Pieces[30], Is.EqualTo(new UgcMapSetItemTable.Piece(1, "a", "e", "x", "")));
        Assert.That(t.Get(2), Is.Null);
        Assert.That(t.Get(3), Is.Null);
        Assert.That(t.Get(4), Is.Null);
        Assert.That(t.Get(5).Pieces.Keys, Is.EqualTo(new uint[] { 40 }));
        Assert.That(t.Get(0), Is.Null);
        Assert.That(UgcMapSetItemTable.Empty.Get(1), Is.Null);
    }
}
