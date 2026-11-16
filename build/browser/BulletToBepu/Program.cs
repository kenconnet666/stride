// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

// Usage: BulletToBepu <folder>...
// Converts the Bullet physics (Stride.Physics) of a game to BepuPhysics (Stride.BepuPhysics), in place: the components
// of its scenes and prefabs, its game settings and the package references of its projects. The scripts are not
// converted (the two APIs differ): the tool lists the ones that use Stride.Physics. Give the folder of the game and
// those of the asset packages it uses, which are converted with it. Run it on a copy, or under version control.

using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Stride.Assets;
using Stride.Assets.Entities;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Definitions;
using Stride.BepuPhysics.Definitions.Colliders;
using Stride.Core.Assets;
using Stride.Core.Reflection;
using Stride.Core.Diagnostics;
using Stride.Core.Yaml;
using Stride.Core.Serialization;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Physics;
using Stride.Physics.Assets;
using Stride.Rendering;
using BulletCharacter = Stride.Physics.CharacterComponent;
using BepuCharacter = Stride.BepuPhysics.CharacterComponent;

if (args.Length == 0 || !args.All(Directory.Exists))
{
    Console.Error.WriteLine("Usage: BulletToBepu <folder>...");
    return 2;
}
var folders = args.Select(Path.GetFullPath).ToArray();
// The names of the files are given from the folder that holds all the others
var root = folders.Aggregate((a, b) => string.Concat(a.Zip(b).TakeWhile(x => x.First == x.Second).Select(x => x.First)));
root = root.EndsWith(Path.DirectorySeparatorChar) ? root : Path.GetDirectoryName(root)!;

// The module initializers register the asset and component types of these assemblies
foreach (var type in new[] { typeof(SceneAsset), typeof(RigidbodyComponent), typeof(ColliderShapeAsset), typeof(BodyComponent) })
    RuntimeHelpers.RunModuleConstructor(type.Module.ModuleHandle);

var warnings = new List<string>();
var converted = 0;
var assets = new List<(string File, AssetLoadResult<Asset> Loaded)>();
foreach (var file in Files("*.*").Where(IsAsset).Order())
{
    var log = new LoggerResult();
    var loaded = AssetFileSerializer.Load<Asset>(file, log);
    if (log.HasErrors)
        warnings.Add($"{Relative(file)}: not converted, it did not load ({string.Join("; ", log.Messages.Where(m => m.IsAtLeast(LogMessageType.Error)).Select(m => m.Text))})");
    else
        assets.Add((file, loaded));
}

// The navigation of Stride.Physics is Bullet's too. A game that has none of its components or meshes loses its
// package and its settings; one that has them is ported to Stride.BepuPhysics.Navigation by hand.
var usesNavigation = Files("*.sdnavmesh").Any(path => !IsBuildOutput(path))
    || assets.Select(x => x.Loaded.Asset).OfType<EntityHierarchyAssetBase>()
        .SelectMany(asset => asset.Hierarchy.Parts.Values).SelectMany(part => part.Entity.Components).Any(IsNavigation);
if (usesNavigation)
    warnings.Add("the game uses the navigation of Stride.Physics (Stride.Navigation): port it to Stride.BepuPhysics.Navigation by hand");

foreach (var (file, loaded) in assets)
{
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

foreach (var project in Files("*.csproj"))
{
    if (!ConvertProject(project)) continue;
    Console.WriteLine($"Converted {Relative(project)}: Stride.Physics is now Stride.BepuPhysics");
}

foreach (var script in Files("*.cs").Where(path => !IsBuildOutput(path)))
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

IEnumerable<string> Files(string pattern) => folders.SelectMany(folder => Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories));

bool IsNavigation(object value) => TypeName(value).StartsWith("Stride.Navigation.", StringComparison.Ordinal);

// The name of the type of a value, also of one whose assembly this tool does not load
static string TypeName(object value) => value is IUnloadable unloadable ? unloadable.TypeName : value.GetType().FullName ?? string.Empty;

bool ConvertSettings(GameSettingsAsset settings)
{
    var bullet = settings.Defaults.Where(x => x is PhysicsSettings || (!usesNavigation && IsNavigation(x))).ToList();
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
        var bullets = entity.Components.OfType<PhysicsComponent>().ToList();
        if (bullets.Count == 0) continue;
        var where = $"{file}: {entity.Name}";
        // Bullet takes several physics components on an entity, Bepu one: the first that moves (or else the first)
        // keeps the shapes of the others
        var main = bullets.FirstOrDefault(x => x is not StaticColliderComponent) ?? bullets[0];
        if (bullets.Any(x => x != main && x is not StaticColliderComponent))
            warnings.Add($"{where}: several moving physics components, merged into one Bepu {main.GetType().Name}: check it");
        else if (main is not StaticColliderComponent && bullets.Count > 1)
            warnings.Add($"{where}: its static colliders became shapes of its {main.GetType().Name}: check it");
        foreach (var bullet in bullets.Where(x => x != main))
        {
            foreach (var shape in bullet.ColliderShapes) main.ColliderShapes.Add(shape);
            entity.Components.Remove(bullet);
        }
        // Bullet scales a shape with its entity, Bepu does not: the scale goes into the sizes
        var scale = WorldScale(entity.Transform, parents);
        ICollider collider;
        if (main is StaticColliderComponent && main.ColliderShapes is [ColliderShapeAssetDesc shapeAsset] && HullModel(shapeAsset, where) is { } model)
        {
            // A static mesh: Bepu collides with the model itself, scaled by its entity
            collider = new MeshCollider { Model = model };
        }
        else
        {
            var compound = new CompoundCollider();
            // A collider keeps the item id of its shape: a prefab instance finds its base collider by it
            CollectionItemIdHelper.TryGetCollectionItemIds(main.ColliderShapes, out var shapeIds);
            var colliderIds = CollectionItemIdHelper.GetCollectionItemIds(compound.Colliders);
            for (var i = 0; i < main.ColliderShapes.Count; i++)
            {
                if (AddShape(compound, main.ColliderShapes[i], scale, where) && shapeIds != null && shapeIds.TryGet(i, out var id))
                    colliderIds[compound.Colliders.Count - 1] = id;
            }
            collider = compound;
        }
        CollidableComponent bepu = main switch
        {
            BulletCharacter character => ConvertCharacter(character, collider, where),
            RigidbodyComponent body => ConvertBody(body, collider, where),
            StaticColliderComponent stat => ConvertStatic(stat, collider, where),
            _ => throw new NotSupportedException($"{where}: {main.GetType().Name} has no Bepu equivalent."),
        };
        bepu.FrictionCoefficient = main.Friction;
        // A Bullet group is one bit, a Bepu layer its index: DefaultFilter is Layer0, KinematicFilter Layer2...
        bepu.CollisionLayer = (CollisionLayer)System.Numerics.BitOperations.Log2((uint)main.CollisionGroup);
        if (main.CanCollideWith != CollisionFilterGroupFlags.AllFilter)
            warnings.Add($"{where}: it collides with {main.CanCollideWith} only, set the collision matrix of the Bepu configuration by hand");
        if (main.Restitution != 0)
            warnings.Add($"{where}: Bepu has no restitution (it was {main.Restitution}), tune SpringFrequency and SpringDampingRatio");
        if (main is StaticColliderComponent { IsTrigger: true } or RigidbodyComponent { IsTrigger: true })
        {
            // A Bepu trigger lets things pass through and raises OnEnter and OnLeave
            bepu.ContactEventHandler = new Trigger();
            warnings.Add($"{where}: a trigger: the scripts that wait for its collisions (NewCollision) subscribe to the OnEnter of its Trigger");
        }
        // The same component for a prefab instance and its base, as before
        bepu.Id = main.Id;
        var index = entity.Components.IndexOf(main);
        entity.Components[index] = bepu;
        changed = true;
    }
    return changed;
}

StaticComponent ConvertStatic(StaticColliderComponent bullet, ICollider collider, string where)
{
    return new StaticComponent { Collider = collider };
}

BodyComponent ConvertBody(RigidbodyComponent bullet, ICollider collider, string where)
{
    if (bullet.OverrideGravity)
        warnings.Add($"{where}: a gravity of its own ({bullet.Gravity}) is not converted");
    if (bullet.LinearDamping != 0 || bullet.AngularDamping != 0)
        warnings.Add($"{where}: damping is per simulation in Bepu (PoseLinearDamping, PoseAngularDamping), not per body");
    if (collider is CompoundCollider compound) DistributeMass(compound, bullet.Mass);
    return new BodyComponent { Collider = collider, Kinematic = bullet.IsKinematic, Gravity = !bullet.OverrideGravity };
}

BepuCharacter ConvertCharacter(BulletCharacter bullet, ICollider collider, string where)
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

// The model of a collider shape asset made of one convex hull of a model, or null after saying why
Model? HullModel(ColliderShapeAssetDesc desc, string where)
{
    var url = AttachedReferenceManager.GetUrl(desc.Shape);
    // A url of another package starts with the name of that package (/PrototypingBlocks/...), and its files are in
    // the asset folder of the package
    var inPackage = url is ['/', ..] ? url[(url.IndexOf('/', 1) + 1)..] : url;
    var file = url is null ? null : Files(Path.GetFileName(url) + ".sdphy")
        .FirstOrDefault(path => !IsBuildOutput(path) && path.Replace('\\', '/').EndsWith("/" + inPackage + ".sdphy", StringComparison.OrdinalIgnoreCase));
    if (file is null)
    {
        warnings.Add($"{where}: its collider shape asset {url} is in none of the folders, add its Bepu collider by hand");
        return null;
    }
    var shapes = AssetFileSerializer.Load<ColliderShapeAsset>(file).Asset.ColliderShapes;
    if (shapes is [ConvexHullColliderShapeDesc { Model: { } model }])
        return model;
    warnings.Add($"{where}: its collider shape asset {url} is not one convex hull of a model, add its Bepu collider by hand");
    return null;
}

// Whether the shape became a collider
bool AddShape(CompoundCollider collider, IInlineColliderShapeDesc shape, Vector3 scale, string where)
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
        return false;
    }
    collider.Colliders.Add(bepu);
    return true;
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

bool ConvertProject(string path)
{
    var document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
    var references = document.Descendants("PackageReference").Where(x => (string?)x.Attribute("Include") == "Stride.Physics").ToList();
    if (references.Count == 0) return false;
    if (!usesNavigation)
        references.AddRange(document.Descendants("PackageReference").Where(x => (string?)x.Attribute("Include") == "Stride.Navigation"));
    foreach (var reference in references)
    {
        if ((string?)reference.Attribute("Include") == "Stride.Navigation")
        {
            Remove(reference);
            continue;
        }
        var hasBepu = document.Descendants("PackageReference").Any(x => (string?)x.Attribute("Include") == "Stride.BepuPhysics");
        if (hasBepu) Remove(reference);
        else reference.SetAttributeValue("Include", "Stride.BepuPhysics");
    }
    static void Remove(XElement element)
    {
        // With the indentation of its line
        if (element.PreviousNode is XText { Value: var text } indentation && string.IsNullOrWhiteSpace(text)) indentation.Remove();
        element.Remove();
    }
    // As it was: no XML declaration, and no byte order mark
    var settings = new System.Xml.XmlWriterSettings { OmitXmlDeclaration = true, Encoding = new System.Text.UTF8Encoding(false) };
    using (var writer = System.Xml.XmlWriter.Create(path, settings))
        document.Save(writer);
    return true;
}
