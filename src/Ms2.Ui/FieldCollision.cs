using Ms2.Formats.Model;
using Ms2.Formats.World;

namespace Ms2.Ui;

/// <summary>
/// Field collision rules the field builder applies per entity.
/// </summary>
public static class FieldCollision
{
    /// <summary>
    /// Whether the entity gets the GeneratePhysX box: CubeMapInformationService::FUN_1410cf160
    /// (cubemapinformationservice_3.cpp:4026-4052) runs FUN_1410c5570, the box, for an MS2CubeProp (0xd8dea70d) and for any
    /// other MS2PhysXProp (0xc9cd2193; MS2CubeProp contains it) whose GeneratePhysX (0x38141815) is set, but not for an
    /// MS2SalePost (0xf01a7608), which it tests first. Nothing there reads the entity's NIF: an entity with no NifAsset
    /// (a trigger mesh with a ProxyNifAsset only) gets its box too. Its caller FUN_1410cef70 skips only an entity
    /// HardwareInstancingService lists as excluded (FUN_1413d7d90), which MS2EntityManagerService::CreateEntity fills
    /// with the entities it makes at run time, not the map's own.
    /// </summary>
    public static bool GeneratesBox(FieldEntity e) =>
        e.Bool("GeneratePhysX") && !e.HasMixin("MS2SalePost") && (e.HasMixin("MS2CubeProp") || e.HasMixin("MS2PhysXProp"));

    /// <summary>
    /// The GeneratePhysX box (CubeMapInformationService::GeneratePhysX): GeneratePhysXDimension (a 150 cube when unset)
    /// standing on the entity position, so its centre is half its height above it, turned by the Z rotation only; the
    /// scale is ignored.
    /// </summary>
    public static (Vec3 Centre, Vec3 Size, double RotZDeg) GeneratedBox(FieldEntity e)
    {
        var d = e.Vector("GeneratePhysXDimension");
        var size = d.X == 0 && d.Y == 0 && d.Z == 0 ? new Vec3(150, 150, 150) : d;
        var p = e.Position;
        return (new Vec3(p.X, p.Y, p.Z + size.Z / 2), size, e.RotationDeg.Z);
    }

    /// <summary>The entity's NIF asset id when it names one (urn:llid:...), else null.</summary>
    public static string NifLlid(FieldEntity e)
    {
        var llid = e.Get("NifAsset");
        return llid != null && llid.StartsWith("urn:llid:") ? llid : null;
    }
}
