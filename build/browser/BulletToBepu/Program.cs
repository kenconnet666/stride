// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

// Usage: BulletToBepu <game folder>
// Converts the Bullet physics (Stride.Physics) of a game to BepuPhysics (Stride.BepuPhysics), in place: the components
// of its scenes and prefabs, its game settings and the package references of its projects. The scripts are not
// converted (the two APIs differ): the tool lists the ones that use Stride.Physics. Run it on a copy, or a game under
// version control.

using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Stride.Assets;
using Stride.Assets.Entities;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions.Colliders;
using Stride.Core.Assets;
using Stride.Core.Diagnostics;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Physics;
using BulletCharacter = Stride.Physics.CharacterComponent;
using BepuCharacter = Stride.BepuPhysics.CharacterComponent;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: BulletToBepu <game folder>");
    return 2;
}
var root = Path.GetFullPath(args[0]);

// The module initializers register the asset and component types of these assemblies
foreach (var type in new[] { typeof(SceneAsset), typeof(RigidbodyComponent), typeof(BodyComponent) })
    RuntimeHelpers.RunModuleConstructor(type.Module.ModuleHandle);

var warnings = new List<string>();
var converted = 0;
foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Where(IsAsset).Order())
{
    var log = new LoggerResult();
    var loaded = AssetFileSerializer.Load<Asset>(file, log);
    if (log.HasErrors)
    {
        warnings.Add($"{Relative(file)}: not converted, it did not load ({string.Join("; ", log.Messages.Where(m => m.IsAtLeast(LogMessageType.Error)).Select(m => m.Text))})");
        continue;
    }
    var changed = loaded.Asset switch
    {
        EntityHierarchyAssetBase hierarchy => ConvertHierarchy(hierarchy, Relative(file)),
        GameSettingsAsset settings => ConvertSettings(settings),
        _ => false,
    };
    if (!changed) continue;
    AssetFileSerializer.Save(file, loaded.Asset, loaded.YamlMetadata);
    converted++;
    Console.WriteLine($"Converted {Relative(file)}");
}

foreach (var project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
{
    if (!ConvertProject(project)) continue;
    Console.WriteLine($"Converted {Relative(project)}: Stride.Physics is now Stride.BepuPhysics");
}

foreach (var script in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories).Where(path => !IsBuildOutput(path)))
{
    if (File.ReadLines(script).Any(line => line.Contains("Stride.Physics", StringComparison.Ordinal)))
        warnings.Add($"{Relative(script)}: uses Stride.Physics, port it to Stride.BepuPhysics by hand");
}

Console.WriteLine($"{converted} asset(s) converted.");
foreach (var warning in warnings) Console.WriteLine("warning: " + warning);
return 0;

bool IsAsset(string path) => !IsBuildOutput(path) && Path.GetExtension(path) is ".sdscene" or ".sdprefab" or ".sdgamesettings";

bool IsBuildOutput(string path) => path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj");

string Relative(string path) => Path.GetRelativePath(root, path);

bool ConvertSettings(GameSettingsAsset settings)
{
    var bullet = settings.Defaults.OfType<PhysicsSettings>().ToList();
    if (bullet.Count == 0) return false;
    foreach (var configuration in bullet) settings.Defaults.Remove(configuration);
    // Bepu creates its configuration with its defaults when the game settings have none
    return true;
}

bool ConvertHierarchy(EntityHierarchyAssetBase asset, string file)
{
    var parents = new Dictionary<TransformComponent, TransformComponent>();
    foreach (var part in asset.Hierarchy.Parts.Values)
        foreach (var child in part.Entity.Transform.Children)
            parents[child] = part.Entity.Transform;

    var changed = false;
    foreach (var part in asset.Hierarchy.Parts.Values)
    {
        var entity = part.Entity;
        foreach (var bullet in entity.Components.OfType<PhysicsComponent>().ToList())
        {
            var where = $"{file}: {entity.Name}";
            // Bullet scales a shape with its entity, Bepu does not: the scale goes into the sizes
            var scale = WorldScale(entity.Transform, parents);
            var collider = new CompoundCollider();
            foreach (var shape in bullet.ColliderShapes)
                AddShape(collider, shape, scale, where);
            CollidableComponent bepu = bullet switch
            {
                BulletCharacter character => ConvertCharacter(character, collider, where),
                RigidbodyComponent body => ConvertBody(body, collider, where),
                StaticColliderComponent stat => ConvertStatic(stat, collider, where),
                _ => throw new NotSupportedException($"{where}: {bullet.GetType().Name} has no Bepu equivalent."),
            };
            bepu.FrictionCoefficient = bullet.Friction;
            if (bullet.CollisionGroup != CollisionFilterGroups.DefaultFilter || bullet.CanCollideWith != CollisionFilterGroupFlags.AllFilter)
                warnings.Add($"{where}: the collision group {bullet.CollisionGroup} is not converted, set its Bepu collision layer by hand");
            if (bullet.Restitution != 0)
                warnings.Add($"{where}: Bepu has no restitution (it was {bullet.Restitution}), tune SpringFrequency and SpringDampingRatio");
            var index = entity.Components.IndexOf(bullet);
            entity.Components[index] = bepu;
            changed = true;
        }
    }
    return changed;
}

StaticComponent ConvertStatic(StaticColliderComponent bullet, CompoundCollider collider, string where)
{
    if (bullet.IsTrigger)
        warnings.Add($"{where}: a trigger needs a Bepu ContactEventHandler, set it by hand");
    return new StaticComponent { Collider = collider };
}

BodyComponent ConvertBody(RigidbodyComponent bullet, CompoundCollider collider, string where)
{
    if (bullet.IsTrigger)
        warnings.Add($"{where}: a trigger needs a Bepu ContactEventHandler, set it by hand");
    if (bullet.OverrideGravity)
        warnings.Add($"{where}: a gravity of its own ({bullet.Gravity}) is not converted");
    if (bullet.LinearDamping != 0 || bullet.AngularDamping != 0)
        warnings.Add($"{where}: damping is per simulation in Bepu (PoseLinearDamping, PoseAngularDamping), not per body");
    DistributeMass(collider, bullet.Mass);
    return new BodyComponent { Collider = collider, Kinematic = bullet.IsKinematic, Gravity = !bullet.OverrideGravity };
}

BepuCharacter ConvertCharacter(BulletCharacter bullet, CompoundCollider collider, string where)
{
    warnings.Add($"{where}: a Bullet character is moved with SetVelocity and Jump, a Bepu one with Move and TryJump: port its script");
    return new BepuCharacter
    {
        Collider = collider,
        JumpForce = bullet.JumpSpeed,
        SlopeAngle = MathUtil.RadiansToDegrees(bullet.MaxSlope.Radians),
    };
}

void DistributeMass(CompoundCollider collider, float mass)
{
    if (collider.Colliders.Count == 0) return;
    foreach (var shape in collider.Colliders) shape.Mass = mass / collider.Colliders.Count;
}

void AddShape(CompoundCollider collider, IInlineColliderShapeDesc shape, Vector3 scale, string where)
{
    ColliderBase? bepu = shape switch
    {
        BoxColliderShapeDesc box => new BoxCollider { Size = box.Size * scale, PositionLocal = box.LocalOffset * scale, RotationLocal = box.LocalRotation },
        SphereColliderShapeDesc sphere => new SphereCollider { Radius = sphere.Radius * MaxComponent(scale), PositionLocal = sphere.LocalOffset * scale },
        CapsuleColliderShapeDesc capsule => new CapsuleCollider
        {
            Radius = capsule.Radius * MaxComponent(scale), Length = capsule.Length * scale.Y,
            PositionLocal = capsule.LocalOffset * scale, RotationLocal = capsule.LocalRotation * AxisRotation(capsule.Orientation),
        },
        CylinderColliderShapeDesc cylinder => new CylinderCollider
        {
            Radius = cylinder.Radius * MaxComponent(scale), Length = cylinder.Height * scale.Y,
            PositionLocal = cylinder.LocalOffset * scale, RotationLocal = cylinder.LocalRotation * AxisRotation(cylinder.Orientation),
        },
        // Bepu has no infinite plane: a box 2 km wide and 1 m thick, its top face on the plane
        StaticPlaneColliderShapeDesc plane => new BoxCollider
        {
            Size = new Vector3(2000, 1, 2000),
            RotationLocal = Quaternion.BetweenDirections(Vector3.UnitY, Vector3.Normalize(plane.Normal)),
            PositionLocal = Vector3.Normalize(plane.Normal) * (plane.Offset - 0.5f),
        },
        _ => null,
    };
    if (bepu is null)
    {
        warnings.Add($"{where}: a {shape.GetType().Name} is not converted, add its Bepu collider by hand");
        return;
    }
    collider.Colliders.Add(bepu);
}

static Quaternion AxisRotation(ShapeOrientation orientation) => orientation switch
{
    // Bepu capsules and cylinders stand on Y
    ShapeOrientation.UpX => Quaternion.RotationZ(-MathUtil.PiOverTwo),
    ShapeOrientation.UpZ => Quaternion.RotationX(MathUtil.PiOverTwo),
    _ => Quaternion.Identity,
};

static float MaxComponent(Vector3 value) => Math.Max(value.X, Math.Max(value.Y, value.Z));

static Vector3 WorldScale(TransformComponent transform, Dictionary<TransformComponent, TransformComponent> parents)
{
    var scale = transform.Scale;
    for (var parent = parents.GetValueOrDefault(transform); parent != null; parent = parents.GetValueOrDefault(parent))
        scale *= parent.Scale;
    return scale;
}

static bool ConvertProject(string path)
{
    var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
    var references = document.Descendants("PackageReference").Where(x => (string?)x.Attribute("Include") == "Stride.Physics").ToList();
    if (references.Count == 0) return false;
    foreach (var reference in references)
    {
        var hasBepu = document.Descendants("PackageReference").Any(x => (string?)x.Attribute("Include") == "Stride.BepuPhysics");
        if (hasBepu) reference.Remove();
        else reference.SetAttributeValue("Include", "Stride.BepuPhysics");
    }
    document.Save(path, SaveOptions.DisableFormatting);
    return true;
}
