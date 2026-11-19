using Ms2.Formats.Text;
using NUnit.Framework;

namespace Ms2.Ui.Tests;

/// <summary>GetFieldMoveErrorMessage's choice of string (FieldMoveError): the keys only, no language text.</summary>
public class FieldMoveErrorTests
{
    [TestCase(8, "s_move_err_no_server")]
    [TestCase(9, "s_move_err_over_user")]
    [TestCase(0x15, "s_move_err_over_user")]
    [TestCase(0x11, "s_move_err_dungeon_not_exist")]
    [TestCase(10, "s_move_err_member_limit")]
    [TestCase(0x16, "s_move_err_member_limit")]
    [TestCase(0xc, "s_move_err_time_out")]
    public void ErrorsByStringId(int error, string key)
    {
        Assert.That(FieldMoveError.StringId(error), Is.Not.Null);
        Assert.That(CommonStringIds.Key(FieldMoveError.StringId(error).Value), Is.EqualTo(key));
        Assert.That(FieldMoveError.Key(error), Is.EqualTo(key));
    }

    [TestCase(0x1b, "s_party_err_not_chief")]
    [TestCase(0x1d, "s_move_err_dungeon_not_exist")]
    [TestCase(7, "s_move_err_field_limit")]
    [TestCase(0x23, "s_move_err_wedding_complete")]
    public void ErrorsByKey(int error, string key)
    {
        Assert.That(FieldMoveError.StringId(error), Is.Null);
        Assert.That(FieldMoveError.Key(error), Is.EqualTo(key));
    }

    [Test]
    public void OtherErrorsTakeTheDefaultWithTheCode()
    {
        Assert.That(FieldMoveError.Key(0x40), Is.EqualTo(FieldMoveError.DefaultKey));
        // a stand-in table: the default string's {0} is the error code; any other string is used as it is
        string Fake(string k) => k == FieldMoveError.DefaultKey ? "<{0}>" : "[" + k + "]";
        Assert.That(FieldMoveError.Text(0x40, Fake), Is.EqualTo("<64>"));
        Assert.That(FieldMoveError.Text(8, Fake), Is.EqualTo("[s_move_err_no_server]"));
        Assert.That(FieldMoveError.Text(0x40, _ => null), Is.EqualTo(""));
    }
}
