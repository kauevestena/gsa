using Godot;

namespace Gsa.Engine.Visuals;

/// <summary>Batches static boxes, cylinders, and triangles by material into indexed ArrayMeshes with world-scale UVs (zero triplanar shader cost).</summary>
public sealed class GsaStaticMeshBatcher
{
    private sealed class Bucket
    {
        public readonly List<Vector3> Vertices = [];
        public readonly List<Vector3> Normals = [];
        public readonly List<Vector2> Uvs = [];
        public readonly List<Color> Colors = [];
        public readonly List<int> Indices = [];
        public bool HasVertexColors;
    }

    private readonly Dictionary<StandardMaterial3D, Bucket> _buckets = new();
    private readonly List<StandardMaterial3D> _order = [];

    private Bucket GetBucket(StandardMaterial3D material)
    {
        if (!_buckets.TryGetValue(material, out var bucket))
        {
            bucket = new Bucket();
            _buckets[material] = bucket;
            _order.Add(material);
        }
        return bucket;
    }

    public void AddBox(
        StandardMaterial3D material,
        Vector3 center,
        Vector3 size,
        Vector3? rotDeg = null,
        float uvScale = 0.1f,
        Color? vertexColor = null)
    {
        var bucket = GetBucket(material);
        var hx = size.X * 0.5f;
        var hy = size.Y * 0.5f;
        var hz = size.Z * 0.5f;
        var hasRot = rotDeg.HasValue && rotDeg.Value != Vector3.Zero;
        var basis = hasRot
            ? Basis.FromEuler(new Vector3(
                Mathf.DegToRad(rotDeg!.Value.X),
                Mathf.DegToRad(rotDeg.Value.Y),
                Mathf.DegToRad(rotDeg.Value.Z)))
            : Basis.Identity;

        var col = vertexColor ?? Colors.White;
        if (vertexColor.HasValue)
            bucket.HasVertexColors = true;

        void AddFace(Vector3 tl, Vector3 tr, Vector3 br, Vector3 bl, Vector3 localNormal, int uvAxis)
        {
            Vector3 TransformPos(Vector3 local) => hasRot ? center + basis * local : center + local;
            var p0 = TransformPos(tl);
            var p1 = TransformPos(tr);
            var p2 = TransformPos(br);
            var p3 = TransformPos(bl);
            var n = hasRot ? (basis * localNormal).Normalized() : localNormal;

            Vector2 CalcUv(Vector3 wp, Vector3 lp) => uvAxis switch
            {
                0 => new Vector2(wp.Z * uvScale, -wp.Y * uvScale), // +/- X walls
                1 => new Vector2(wp.X * uvScale, wp.Z * uvScale),  // +/- Y horizontal
                _ => new Vector2(wp.X * uvScale, -wp.Y * uvScale), // +/- Z walls
            };

            var baseIdx = bucket.Vertices.Count;
            bucket.Vertices.Add(p0);
            bucket.Vertices.Add(p1);
            bucket.Vertices.Add(p2);
            bucket.Vertices.Add(p3);

            bucket.Normals.Add(n);
            bucket.Normals.Add(n);
            bucket.Normals.Add(n);
            bucket.Normals.Add(n);

            bucket.Uvs.Add(CalcUv(p0, tl));
            bucket.Uvs.Add(CalcUv(p1, tr));
            bucket.Uvs.Add(CalcUv(p2, br));
            bucket.Uvs.Add(CalcUv(p3, bl));

            bucket.Colors.Add(col);
            bucket.Colors.Add(col);
            bucket.Colors.Add(col);
            bucket.Colors.Add(col);

            // Clockwise front-face winding matching Godot BoxMesh
            bucket.Indices.Add(baseIdx);
            bucket.Indices.Add(baseIdx + 1);
            bucket.Indices.Add(baseIdx + 3);
            bucket.Indices.Add(baseIdx + 1);
            bucket.Indices.Add(baseIdx + 2);
            bucket.Indices.Add(baseIdx + 3);
        }

        // +Z, -Z, +X, -X, +Y, -Y
        AddFace(new(-hx, hy, hz), new(hx, hy, hz), new(hx, -hy, hz), new(-hx, -hy, hz), Vector3.Back, 2);
        AddFace(new(hx, hy, -hz), new(-hx, hy, -hz), new(-hx, -hy, -hz), new(hx, -hy, -hz), Vector3.Forward, 2);
        AddFace(new(hx, hy, hz), new(hx, hy, -hz), new(hx, -hy, -hz), new(hx, -hy, hz), Vector3.Right, 0);
        AddFace(new(-hx, hy, -hz), new(-hx, hy, hz), new(-hx, -hy, hz), new(-hx, -hy, -hz), Vector3.Left, 0);
        AddFace(new(-hx, hy, -hz), new(hx, hy, -hz), new(hx, hy, hz), new(-hx, hy, hz), Vector3.Up, 1);
        AddFace(new(-hx, -hy, hz), new(hx, -hy, hz), new(hx, -hy, -hz), new(-hx, -hy, -hz), Vector3.Down, 1);
    }

    public void AddBoxNormalizedUv(
        StandardMaterial3D material,
        Vector3 center,
        Vector3 size)
    {
        var bucket = GetBucket(material);
        var hx = size.X * 0.5f;
        var hy = size.Y * 0.5f;
        var hz = size.Z * 0.5f;

        void AddFace(Vector3 tl, Vector3 tr, Vector3 br, Vector3 bl, Vector3 n)
        {
            var baseIdx = bucket.Vertices.Count;
            bucket.Vertices.Add(center + tl);
            bucket.Vertices.Add(center + tr);
            bucket.Vertices.Add(center + br);
            bucket.Vertices.Add(center + bl);
            bucket.Normals.Add(n);
            bucket.Normals.Add(n);
            bucket.Normals.Add(n);
            bucket.Normals.Add(n);
            bucket.Uvs.Add(new Vector2(0f, 0f));
            bucket.Uvs.Add(new Vector2(1f, 0f));
            bucket.Uvs.Add(new Vector2(1f, 1f));
            bucket.Uvs.Add(new Vector2(0f, 1f));
            bucket.Colors.Add(Colors.White);
            bucket.Colors.Add(Colors.White);
            bucket.Colors.Add(Colors.White);
            bucket.Colors.Add(Colors.White);
            bucket.Indices.Add(baseIdx);
            bucket.Indices.Add(baseIdx + 1);
            bucket.Indices.Add(baseIdx + 3);
            bucket.Indices.Add(baseIdx + 1);
            bucket.Indices.Add(baseIdx + 2);
            bucket.Indices.Add(baseIdx + 3);
        }

        AddFace(new(-hx, hy, hz), new(hx, hy, hz), new(hx, -hy, hz), new(-hx, -hy, hz), Vector3.Back);
        AddFace(new(hx, hy, -hz), new(-hx, hy, -hz), new(-hx, -hy, -hz), new(hx, -hy, -hz), Vector3.Forward);
        AddFace(new(hx, hy, hz), new(hx, hy, -hz), new(hx, -hy, -hz), new(hx, -hy, hz), Vector3.Right);
        AddFace(new(-hx, hy, -hz), new(-hx, hy, hz), new(-hx, -hy, hz), new(-hx, -hy, -hz), Vector3.Left);
        AddFace(new(-hx, hy, -hz), new(hx, hy, -hz), new(hx, hy, hz), new(-hx, hy, hz), Vector3.Up);
        AddFace(new(-hx, -hy, hz), new(hx, -hy, hz), new(hx, -hy, -hz), new(-hx, -hy, -hz), Vector3.Down);
    }

    public void AddCylinder(
        StandardMaterial3D material,
        Vector3 center,
        float topRadius,
        float bottomRadius,
        float height,
        int radialSegments = 10,
        float uvScale = 0.1f,
        Color? vertexColor = null)
    {
        var bucket = GetBucket(material);
        var hy = height * 0.5f;
        var col = vertexColor ?? Colors.White;
        if (vertexColor.HasValue)
            bucket.HasVertexColors = true;

        for (var i = 0; i < radialSegments; i++)
        {
            var a0 = i * MathF.Tau / radialSegments;
            var a1 = (i + 1) * MathF.Tau / radialSegments;
            var c0 = MathF.Cos(a0);
            var s0 = MathF.Sin(a0);
            var c1 = MathF.Cos(a1);
            var s1 = MathF.Sin(a1);

            var tl = center + new Vector3(c0 * topRadius, hy, s0 * topRadius);
            var tr = center + new Vector3(c1 * topRadius, hy, s1 * topRadius);
            var br = center + new Vector3(c1 * bottomRadius, -hy, s1 * bottomRadius);
            var bl = center + new Vector3(c0 * bottomRadius, -hy, s0 * bottomRadius);

            // Side quad (clockwise from outside when angles increase around +Y: c0,s0 -> c1,s1 goes CW from outside!)
            var n0 = new Vector3(c0, 0f, s0);
            var n1 = new Vector3(c1, 0f, s1);
            var baseIdx = bucket.Vertices.Count;
            // Looking from outside (+radial): left is a1, right is a0! Wait: (c0,0,s0) at a=0 is (1,0,0); at a=PI/2 is (0,0,1).
            // Going from a0 to a1 turns toward +Z (right when looking from +X!).
            // Let's verify: tl=(1,h,0), tr=(0,h,1), bl=(1,-h,0): (tr-tl)=(-1,0,1), (bl-tl)=(0,-2h,0).
            // (-1,0,1) x (0,-2h,0) = (2h, 0, 2h) -> +normal! So swap tr/tl -> tl=a1, tr=a0 so cross is -normal!
            bucket.Vertices.Add(tr);
            bucket.Vertices.Add(tl);
            bucket.Vertices.Add(bl);
            bucket.Vertices.Add(br);
            bucket.Normals.Add(n1);
            bucket.Normals.Add(n0);
            bucket.Normals.Add(n0);
            bucket.Normals.Add(n1);
            bucket.Uvs.Add(new Vector2((i + 1) * uvScale, -tr.Y * uvScale));
            bucket.Uvs.Add(new Vector2(i * uvScale, -tl.Y * uvScale));
            bucket.Uvs.Add(new Vector2(i * uvScale, -bl.Y * uvScale));
            bucket.Uvs.Add(new Vector2((i + 1) * uvScale, -br.Y * uvScale));
            bucket.Colors.Add(col);
            bucket.Colors.Add(col);
            bucket.Colors.Add(col);
            bucket.Colors.Add(col);
            bucket.Indices.Add(baseIdx);
            bucket.Indices.Add(baseIdx + 1);
            bucket.Indices.Add(baseIdx + 3);
            bucket.Indices.Add(baseIdx + 1);
            bucket.Indices.Add(baseIdx + 2);
            bucket.Indices.Add(baseIdx + 3);

            // Top cap triangle
            var topCenter = center + new Vector3(0f, hy, 0f);
            var topIdx = bucket.Vertices.Count;
            bucket.Vertices.Add(topCenter);
            bucket.Vertices.Add(tl);
            bucket.Vertices.Add(tr);
            bucket.Normals.Add(Vector3.Up);
            bucket.Normals.Add(Vector3.Up);
            bucket.Normals.Add(Vector3.Up);
            bucket.Uvs.Add(new Vector2(topCenter.X * uvScale, topCenter.Z * uvScale));
            bucket.Uvs.Add(new Vector2(tl.X * uvScale, tl.Z * uvScale));
            bucket.Uvs.Add(new Vector2(tr.X * uvScale, tr.Z * uvScale));
            bucket.Colors.Add(col);
            bucket.Colors.Add(col);
            bucket.Colors.Add(col);
            // (tl - center) x (tr - center) = (1,0,0) x (0,0,1) = (0,-1,0) = -Up! Correct CW!
            bucket.Indices.Add(topIdx);
            bucket.Indices.Add(topIdx + 1);
            bucket.Indices.Add(topIdx + 2);
        }
    }

    public ArrayMesh? BuildCombinedArrayMesh()
    {
        if (_order.Count == 0)
            return null;

        var mesh = new ArrayMesh();
        var surfIndex = 0;
        foreach (var mat in _order)
        {
            var b = _buckets[mat];
            if (b.Vertices.Count == 0)
                continue;

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = b.Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = b.Normals.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = b.Uvs.ToArray();
            if (b.HasVertexColors)
                arrays[(int)Mesh.ArrayType.Color] = b.Colors.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = b.Indices.ToArray();

            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(surfIndex, mat);
            surfIndex++;
        }
        return surfIndex > 0 ? mesh : null;
    }

    public void BuildSeparatedNodes(
        Node3D parent,
        string namePrefix,
        GeometryInstance3D.ShadowCastingSetting castShadow = GeometryInstance3D.ShadowCastingSetting.On,
        float visibilityRangeEnd = 0f)
    {
        var idx = 0;
        foreach (var mat in _order)
        {
            var b = _buckets[mat];
            if (b.Vertices.Count == 0)
                continue;

            var mesh = new ArrayMesh();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = b.Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = b.Normals.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = b.Uvs.ToArray();
            if (b.HasVertexColors)
                arrays[(int)Mesh.ArrayType.Color] = b.Colors.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = b.Indices.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(0, mat);

            var mi = new MeshInstance3D
            {
                Name = $"{namePrefix}_{idx++}",
                Mesh = mesh,
                CastShadow = castShadow,
            };
            if (visibilityRangeEnd > 0f)
            {
                mi.VisibilityRangeEnd = visibilityRangeEnd;
                mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
            }
            parent.AddChild(mi);
        }
    }

    public MeshInstance3D? BuildSingleMeshInstance(
        string name,
        GeometryInstance3D.ShadowCastingSetting castShadow = GeometryInstance3D.ShadowCastingSetting.On,
        float visibilityRangeEnd = 0f)
    {
        var mesh = BuildCombinedArrayMesh();
        if (mesh is null)
            return null;

        var mi = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            CastShadow = castShadow,
        };
        if (visibilityRangeEnd > 0f)
        {
            mi.VisibilityRangeEnd = visibilityRangeEnd;
            mi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
        }
        return mi;
    }
}

