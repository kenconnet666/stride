// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

// Usage: BulletToBepu <folder>...
// Converts the Bullet physics (Stride.Physics) of a game to BepuPhysics (Stride.BepuPhysics), in place: the components
// of its scenes and prefabs, its game settings and the package references of its projects. Its navigation
// (Stride.Navigation) goes to Stride.BepuPhysics.Navigation, which builds the navigation mesh at runtime: the
// navigation mesh assets are removed. The scripts are not converted (the two APIs differ): the tool lists the ones
// that use Stride.Physics or Stride.Navigation. Give the folder of the game and those of the asset packages it uses,
// which are converted with it. Run it on a copy, or under version control.

using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Stride.Assets;
using Stride.Assets.Entities;
using Stride.BepuPhysics;
using Stride.BepuPhysics.Assets;
using Stride.BepuPhysics.Definitions;
using Stride.BepuPhysics.Definitions.Colliders;
using Stride.BepuPhysics.Navigation.Components;
using Stride.BepuPhysics.Navigation.Definitions;
using Stride.Core.Assets;
using Stride.Core.Assets.Yaml;
using Stride.Core.Reflection;
using Stride.Core.Diagnostics;
using Stride.Core.Yaml;
using Stride.Core.Serialization;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Navigation;
using Stride.Navigation.Assets;
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
foreach (var type in new[] { typeof(SceneAsset), typeof(RigidbodyComponent), typeof(ColliderShapeAsset), typeof(BodyComponent), typeof(HullAsset), typeof(NavigationComponent), typeof(RecastNavigationComponent) })
    RuntimeHelpers.RunModuleConstructor(type.Module.ModuleHandle);
// The editor registers the assembly of the Bepu assets when it loads the package that brings it
AssemblyRegistry.Register(typeof(HullAsset).Assembly, AssemblyCommonCategories.Assets);

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
// package and its settings; one that has them goes to Stride.BepuPhysics.Navigation.
var navigationMeshes = Files("*" + NavigationMeshAsset.FileExtension).Where(path => !IsBuildOutput(path)).ToList();
var usesNavigation = navigationMeshes.Count > 0
    || assets.Select(x => x.Loaded.Asset).OfType<EntityHierarchyAssetBase>()
        .SelectMany(asset => asset.Hierarchy.Parts.Values).SelectMany(part => part.Entity.Components).Any(IsNavigation);
// Bepu builds one navigation mesh at runtime, from the settings of the game: those of the mesh asset, when there is one
var navigationMesh = navigationMeshes.Count == 1 ? AssetFileSerializer.Load<NavigationMeshAsset>(navigationMeshes[0]).Asset : null;
if (navigationMeshes.Count > 1)
    warnings.Add("the game has several navigation meshes, Bepu builds one: check the RecastNavigationConfiguration of its game settings");
if (usesNavigation)
    warnings.Add("Bepu builds the navigation mesh at runtime: call RebuildNavMesh of the RecastMeshSystem service when the scene is loaded, and when its static colliders change");

// Bullet: A and B collide when the mask of A has the group of B and the mask of B the group of A. Bepu has one bit per
// pair of layers: the mask of a group is the union of those of its components.
var masks = new Dictionary<int, uint>();
var mixedMasks = new HashSet<int>();
foreach (var bullet in assets.Select(x => x.Loaded.Asset).OfType<EntityHierarchyAssetBase>()
    .SelectMany(asset => asset.Hierarchy.Parts.Values).SelectMany(part => part.Entity.Components).OfType<PhysicsComponent>())
{
    var layer = System.Numerics.BitOperations.Log2((uint)bullet.CollisionGroup);
    var mask = (uint)bullet.CanCollideWith;
    if (masks.TryGetValue(layer, out var known) && known != mask) mixedMasks.Add(layer);
    masks[layer] = known | mask;
}
foreach (var layer in mixedMasks)
    warnings.Add($"the components of collision layer {layer} collide with different groups: the Bepu collision matrix lets that layer collide with all of them");

foreach (var (file, loaded) in assets)
{
    var changed = loaded.Asset switch
    {
        EntityHierarchyAssetBase hierarchy => ConvertHierarchy(hierarchy, loaded.YamlMetadata, Relative(file)),
        GameSettingsAsset settings => ConvertSettings(settings),
        _ => false,
    };
    if (!changed) continue;
    AssetFileSerializer.Save(file, loaded.Asset, loaded.YamlMetadata);
    converted++;
    Console.WriteLine($"Converted {Relative(file)}");
}

foreach (var file in navigationMeshes)
{
    File.Delete(file);
    Console.WriteLine($"Removed {Relative(file)}: Bepu builds the navigation mesh at runtime");
}

foreach (var project in Files("*.csproj"))
{
    if (!ConvertProject(project)) continue;
    Console.WriteLine($"Converted {Relative(project)}: Stride.Physics is now Stride.BepuPhysics");
}

foreach (var script in Files("*.cs").Where(path => !IsBuildOutput(path)))
{
    if (File.ReadLines(script).Any(line => line.Contains("Stride.Physics", StringComparison.Ordinal) || line.Contains("Stride.Navigation", StringComparison.Ordinal)))
        warnings.Add($"{Relative(script)}: uses Stride.Physics or Stride.Navigation, port it to Stride.BepuPhysics by hand");
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
    var bullet = settings.Defaults.Where(x => x is PhysicsSettings || IsNavigation(x)).ToList();
    if (bullet.Count == 0) return false;
    foreach (var configuration in bullet) settings.Defaults.Remove(configuration);
    if (usesNavigation)
        settings.Defaults.Add(ConvertNavigationSettings(bullet.OfType<NavigationSettings>().FirstOrDefault()));
    if (bullet.OfType<PhysicsSettings>().FirstOrDefault() is { } physics)
    {
        var simulation = new BepuSimulation { PoseGravity = physics.Gravity, FixedTimeStepSeconds = physics.FixedTimeStep };
        if (masks.Values.Any(mask => mask != (uint)CollisionFilterGroupFlags.AllFilter))
        {
            // Bullet has 16 groups
            for (var a = 0; a < 16; a++)
            for (var b = a; b < 16; b++)
            {
                var collide = (masks.GetValueOrDefault(a, ~0u) & (1u << b)) != 0 && (masks.GetValueOrDefault(b, ~0u) & (1u << a)) != 0;
                simulation.CollisionMatrix.Set((CollisionLayer)a, (CollisionLayer)b, collide);
            }
            foreach (var (layer, mask) in masks.Where(x => x.Value != (uint)CollisionFilterGroupFlags.AllFilter).OrderBy(x => x.Key))
                Console.WriteLine($"Collision layer {layer} collides with {simulation.CollisionMatrix.Get((CollisionLayer)layer)}");
        }
        settings.Defaults.Add(new BepuConfiguration { BepuSimulations = [simulation] });
    }
    return true;
}

bool ConvertHierarchy(EntityHierarchyAssetBase asset, AttachedYamlAssetMetadata metadata, string file)
{
    var parents = new Dictionary<TransformComponent, TransformComponent>();
    foreach (var part in asset.Hierarchy.Parts.Values)
        foreach (var child in part.Entity.Transform.Children)
            parents[child] = part.Entity.Transform;

    var changed = ConvertNavigation(asset);
    var convertedIds = new HashSet<ItemId>();
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
                // A shape asset made of primitive shapes is converted as if they were inline
                var shape = main.ColliderShapes[i];
                var shapes = shape is ColliderShapeAssetDesc wrapper && AssetShapes(wrapper, where) is { } inner && inner.All(IsPrimitive)
                    ? inner.Cast<IInlineColliderShapeDesc>().ToList() : [shape];
                var first = compound.Colliders.Count;
                foreach (var inlineShape in shapes)
                    AddShape(compound, inlineShape, scale, where);
                if (compound.Colliders.Count > first && shapeIds != null && shapeIds.TryGet(i, out var id))
                    colliderIds[first] = id;
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
        if (CollectionItemIdHelper.TryGetCollectionItemIds(entity.Components, out var componentIds) && componentIds.TryGet(index, out var componentId))
            convertedIds.Add(componentId);
        changed = true;
    }
    if (changed)
        ConvertOverrides(metadata, convertedIds, file);
    return changed;
}

// A prefab instance marks the members it sets itself (CollisionGroup*: in YAML): those of the converted components
// move to their Bepu members. The ids are those of the components in their entities, the same for all the instances
// of a prefab.
void ConvertOverrides(AttachedYamlAssetMetadata metadata, HashSet<ItemId> convertedIds, string file)
{
    if (metadata.RetrieveMetadata(AssetObjectSerializerBackend.OverrideDictionaryKey) is not { } overrides) return;
    foreach (var (path, type) in overrides.ToList())
    {
        var elements = path.Elements;
        var at = Enumerable.Range(0, Math.Max(0, elements.Count - 2)).FirstOrDefault(i =>
            elements[i] is { Type: YamlAssetPath.ElementType.Member } component && component.AsMember() == nameof(Entity.Components)
            && elements[i + 1].Type == YamlAssetPath.ElementType.ItemId && convertedIds.Contains(elements[i + 1].AsItemId())
            && elements[i + 2].Type == YamlAssetPath.ElementType.Member, -1);
        if (at < 0) continue;
        overrides.Remove(path);
        var bulletMember = elements[at + 2].AsMember();
        var bepuMember = bulletMember switch
        {
            nameof(PhysicsComponent.CollisionGroup) => nameof(CollidableComponent.CollisionLayer),
            nameof(PhysicsComponent.Friction) => nameof(CollidableComponent.FrictionCoefficient),
            nameof(StaticColliderComponent.IsTrigger) => nameof(CollidableComponent.ContactEventHandler),
            nameof(PhysicsComponent.ColliderShapes) => nameof(CollidableComponent.Collider),
            // The collision matrix of the simulation replaces the masks
            nameof(PhysicsComponent.CanCollideWith) => "",
            _ => null,
        };
        if (bepuMember is null)
            warnings.Add($"{file}: a prefab instance sets its own {bulletMember}, which has no Bepu member: check it");
        if (string.IsNullOrEmpty(bepuMember)) continue;
        var bepuPath = new YamlAssetPath(elements.Take(at + 2));
        bepuPath.PushMember(bepuMember);
        overrides.Set(bepuPath, type);
    }
}

// The navigation components, with their ids: a prefab instance finds its base component by it
bool ConvertNavigation(EntityHierarchyAssetBase asset)
{
    var changed = false;
    foreach (var entity in asset.Hierarchy.Parts.Values.Select(part => part.Entity))
    {
        for (var i = 0; i < entity.Components.Count; i++)
        {
            EntityComponent? bepu = entity.Components[i] switch
            {
                NavigationComponent => new RecastNavigationComponent(),
                NavigationBoundingBoxComponent box => new BepuNavigationBoundingBoxComponent { Size = box.Size },
                _ => null,
            };
            if (bepu is null) continue;
            bepu.Id = entity.Components[i].Id;
            entity.Components[i] = bepu;
            changed = true;
        }
    }
    return changed;
}

RecastNavigationConfiguration ConvertNavigationSettings(NavigationSettings? bullet)
{
    var configuration = new RecastNavigationConfiguration();
    var build = configuration.BuildSettings;
    if ((navigationMesh?.BuildSettings ?? bullet?.BuildSettings) is { } settings)
    {
        build.CellSize = settings.CellSize;
        build.CellHeight = settings.CellHeight;
        build.MinRegionSize = settings.MinRegionArea;
        build.MergedRegionSize = settings.RegionMergeArea;
        build.EdgeMaxLen = settings.MaxEdgeLen;
        build.EdgeMaxError = settings.MaxEdgeError;
        build.DetailSampleDist = settings.DetailSamplingDistance;
        build.DetailSampleMaxError = settings.MaxDetailSamplingError;
    }
    // A Bullet group is one bit, a Bepu layer its index: the flags are the mask
    var groups = navigationMesh?.IncludedCollisionGroups ?? bullet?.IncludedCollisionGroups ?? CollisionFilterGroupFlags.AllFilter;
    build.IncludedLayers = groups == CollisionFilterGroupFlags.AllFilter ? CollisionMask.Everything : (CollisionMask)(uint)groups;
    if (bullet?.Groups.Count > 1)
        warnings.Add("the navigation has several groups of agents, Bepu one: it takes the agent of the first");
    if (bullet?.Groups.FirstOrDefault()?.AgentSettings is { } agent)
    {
        build.AgentHeight = agent.Height;
        build.AgentRadius = agent.Radius;
        build.AgentMaxClimb = agent.MaxClimb;
        build.AgentMaxSlope = agent.MaxSlope.Degrees;
    }
    return configuration;
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
    // Bullet's slope only limits what the character climbs; Bepu's also decides what it stands on, and a slope of 0
    // supports nothing: the character is always in the air (slow, without friction, no jump). Its default then.
    var slope = bullet.MaxSlope.Degrees;
    var character = new BepuCharacter { Collider = collider, JumpForce = bullet.JumpSpeed };
    if (slope >= 1) character.SlopeAngle = Math.Min(slope, 89);
    return character;
}

void DistributeMass(CompoundCollider collider, float mass)
{
    if (collider.Colliders.Count == 0) return;
    foreach (var shape in collider.Colliders) shape.Mass = mass / collider.Colliders.Count;
}

static bool IsPrimitive(IAssetColliderShapeDesc shape)
    => shape is BoxColliderShapeDesc or SphereColliderShapeDesc or CapsuleColliderShapeDesc or CylinderColliderShapeDesc or StaticPlaneColliderShapeDesc;

// The model of a collider shape asset made of one convex hull of a model, or null after saying why
Model? HullModel(ColliderShapeAssetDesc desc, string where)
{
    var shapes = AssetShapes(desc, where);
    if (shapes is null) return null;
    if (shapes is [ConvexHullColliderShapeDesc { Model: { } model }])
        return model;
    if (!shapes.All(IsPrimitive))
        warnings.Add($"{where}: its collider shape asset {AttachedReferenceManager.GetUrl(desc.Shape)} is not one convex hull of a model, add its Bepu collider by hand");
    return null;
}

// The shapes of a collider shape asset, or null after saying why
List<IAssetColliderShapeDesc>? AssetShapes(ColliderShapeAssetDesc desc, string where)
    => ShapeAssetFile(desc, where) is { } file ? AssetFileSerializer.Load<ColliderShapeAsset>(file).Asset.ColliderShapes.ToList() : null;

// The file of a collider shape asset, or null after saying why
string? ShapeAssetFile(ColliderShapeAssetDesc desc, string where)
{
    var url = AttachedReferenceManager.GetUrl(desc.Shape);
    // A url of another package starts with the name of that package (/PrototypingBlocks/...), and its files are in
    // the asset folder of the package
    var inPackage = url is ['/', ..] ? url[(url.IndexOf('/', 1) + 1)..] : url;
    var file = url is null ? null : Files(Path.GetFileName(url) + ".sdphy")
        .FirstOrDefault(path => !IsBuildOutput(path) && path.Replace('\\', '/').EndsWith("/" + inPackage + ".sdphy", StringComparison.OrdinalIgnoreCase));
    if (file is null)
        warnings.Add($"{where}: its collider shape asset {url} is in none of the folders, add its Bepu collider by hand");
    return file;
}

// A Bepu convex hull collider for a collider shape asset made of one convex hull of a model, or null. Its hull is a
// Bepu hull asset next to the shape asset, made once for each scale: Bepu does not scale a collider with its entity.
ConvexHullCollider? HullCollider(ColliderShapeAssetDesc desc, Vector3 scale, string where)
{
    if (ShapeAssetFile(desc, where) is not { } file
        || AssetFileSerializer.Load<ColliderShapeAsset>(file).Asset.ColliderShapes is not [ConvexHullColliderShapeDesc { Model: { } model } bullet])
        return null;
    var suffix = scale == Vector3.One ? "" : FormattableString.Invariant($"-{scale.X:0.###}x{scale.Y:0.###}x{scale.Z:0.###}");
    var name = Path.GetFileNameWithoutExtension(file) + "Hull" + suffix;
    var hullFile = Path.Combine(Path.GetDirectoryName(file)!, name + HullAsset.FileExtension);
    var url = AttachedReferenceManager.GetUrl(desc.Shape)!;
    var hullUrl = url[..(url.LastIndexOf('/') + 1)] + name;
    AssetId id;
    if (File.Exists(hullFile))
    {
        id = AssetFileSerializer.Load<HullAsset>(hullFile).Asset.Id;
    }
    else
    {
        var hull = new HullAsset
        {
            Model = model,
            LocalOffset = bullet.LocalOffset * scale,
            LocalRotation = bullet.LocalRotation,
            Scaling = bullet.Scaling * scale,
        };
        hull.Decomposition.Enabled = bullet.Decomposition.Enabled;
        AssetFileSerializer.Save(hullFile, hull, null);
        id = hull.Id;
        Console.WriteLine($"Created {Relative(hullFile)}: the Bepu hull of {Relative(file)}");
    }
    return new ConvexHullCollider { Hull = AttachedReferenceManager.CreateProxyObject<DecomposedHulls>(id, hullUrl) };
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
        ColliderShapeAssetDesc asset when HullCollider(asset, scale, where) is { } hull => hull,
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
    var references = document.Descendants("PackageReference").Where(x => (string?)x.Attribute("Include") is "Stride.Physics" or "Stride.Navigation").ToList();
    if (references.Count == 0) return false;
    foreach (var reference in references)
    {
        var bepuPackage = (string?)reference.Attribute("Include") == "Stride.Physics" ? "Stride.BepuPhysics" : "Stride.BepuPhysics.Navigation";
        if (bepuPackage == "Stride.BepuPhysics.Navigation" && !usesNavigation)
            Remove(reference);
        else if (document.Descendants("PackageReference").Any(x => (string?)x.Attribute("Include") == bepuPackage))
            Remove(reference);
        else
            reference.SetAttributeValue("Include", bepuPackage);
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
