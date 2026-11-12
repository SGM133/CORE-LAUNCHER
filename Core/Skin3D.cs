using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CoreLauncher.Core;

/// <summary>
/// Объёмная модель игрока из текстуры скина: голова, тело, руки, ноги, второй слой и плащ.
/// Координаты как в игре: 1 единица = 1 пиксель скина, персонаж смотрит на зрителя (+Z), ноги стоят на Y = 0.
/// </summary>
public static class Skin3D
{
    const int Up = 8;   // во столько раз увеличиваем текстуру, чтобы пиксели оставались чёткими

    sealed class Mesh
    {
        public readonly MeshGeometry3D G = new();
        readonly double _tw, _th;
        public Mesh(double texW, double texH) { _tw = texW; _th = texH; }

        /// <summary>Грань: углы по порядку (левый верх, правый верх, правый низ, левый низ), если смотреть снаружи.</summary>
        public void Quad(Point3D tl, Point3D tr, Point3D br, Point3D bl, double u, double v, double w, double h, bool mirror)
        {
            const double e = 0.03;   // чуть внутрь, чтобы соседние пиксели текстуры не просачивались
            double u0 = (u + e) / _tw, u1 = (u + w - e) / _tw, v0 = (v + e) / _th, v1 = (v + h - e) / _th;
            if (mirror) (u0, u1) = (u1, u0);
            int i = G.Positions.Count;
            G.Positions.Add(tl); G.Positions.Add(tr); G.Positions.Add(br); G.Positions.Add(bl);
            G.TextureCoordinates.Add(new Point(u0, v0)); G.TextureCoordinates.Add(new Point(u1, v0));
            G.TextureCoordinates.Add(new Point(u1, v1)); G.TextureCoordinates.Add(new Point(u0, v1));
            G.TriangleIndices.Add(i); G.TriangleIndices.Add(i + 3); G.TriangleIndices.Add(i + 2);
            G.TriangleIndices.Add(i); G.TriangleIndices.Add(i + 2); G.TriangleIndices.Add(i + 1);
        }

        /// <summary>
        /// Параллелепипед размером w*h*d (в пикселях скина) с развёрткой Minecraft, начинающейся в (u, v).
        /// grow - насколько раздуть (второй слой). mirror - зеркальная развёртка (левая рука/нога старых скинов).
        /// </summary>
        public void Box(double cx, double y0, double cz, double w, double h, double d, double u, double v, double grow = 0, bool mirror = false)
        {
            double x0 = cx - w / 2 - grow, x1 = cx + w / 2 + grow, yb = y0 - grow, yt = y0 + h + grow, z0 = cz - d / 2 - grow, z1 = cz + d / 2 + grow;
            Point3D P(double x, double y, double z) => new(x, y, z);
            // боковые полосы развёртки при зеркалировании меняются местами
            double uRight = mirror ? u + d + w : u, uLeft = mirror ? u : u + d + w;
            Quad(P(x0, yt, z1), P(x1, yt, z1), P(x1, yb, z1), P(x0, yb, z1), u + d, v + d, w, h, mirror);               // перед
            Quad(P(x1, yt, z0), P(x0, yt, z0), P(x0, yb, z0), P(x1, yb, z0), u + d + w + d, v + d, w, h, mirror);       // спина
            Quad(P(x0, yt, z0), P(x0, yt, z1), P(x0, yb, z1), P(x0, yb, z0), uRight, v + d, d, h, mirror);              // правый бок персонажа
            Quad(P(x1, yt, z1), P(x1, yt, z0), P(x1, yb, z0), P(x1, yb, z1), uLeft, v + d, d, h, mirror);               // левый бок
            Quad(P(x0, yt, z0), P(x1, yt, z0), P(x1, yt, z1), P(x0, yt, z1), u + d, v, w, d, mirror);                   // верх
            Quad(P(x0, yb, z1), P(x1, yb, z1), P(x1, yb, z0), P(x0, yb, z0), u + d + w, v, w, d, mirror);               // низ
        }
    }

    static Px Upscale(Px src)
    {
        var dst = new Px(src.W * Up, src.H * Up);
        for (int y = 0; y < dst.H; y++)
        {
            int sy = y / Up;
            for (int x = 0; x < dst.W; x++)
                Buffer.BlockCopy(src.D, (sy * src.W + x / Up) * 4, dst.D, (y * dst.W + x) * 4, 4);
        }
        return dst;
    }

    static Material Mat(Px tex)
    {
        var brush = new ImageBrush(Upscale(tex).ToBitmap())
        {
            ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1),
            Stretch = Stretch.Fill, TileMode = TileMode.None,
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        var m = new DiffuseMaterial(brush);
        m.Freeze();
        return m;
    }

    public static Model3DGroup Build(Px skin, Px? cape, bool slim)
    {
        bool legacy = skin.H == 32;          // старый формат 64x32: левые конечности - зеркало правых, второй слой только у головы
        double aw = slim ? 3 : 4;            // ширина руки
        var mat = Mat(skin);

        var body = new Mesh(64, skin.H);
        body.Box(0, 24, 0, 8, 8, 8, 0, 0);                               // голова
        body.Box(0, 12, 0, 8, 12, 4, 16, 16);                            // тело
        body.Box(-4 - aw / 2, 12, 0, aw, 12, 4, 40, 16);                 // правая рука
        body.Box(-2, 0, 0, 4, 12, 4, 0, 16);                             // правая нога
        if (legacy)
        {
            body.Box(4 + aw / 2, 12, 0, aw, 12, 4, 40, 16, 0, true);
            body.Box(2, 0, 0, 4, 12, 4, 0, 16, 0, true);
        }
        else
        {
            body.Box(4 + aw / 2, 12, 0, aw, 12, 4, 32, 48);
            body.Box(2, 0, 0, 4, 12, 4, 16, 48);
        }

        var over = new Mesh(64, skin.H);
        over.Box(0, 24, 0, 8, 8, 8, 32, 0, 0.5);                         // "шапка"
        if (!legacy)
        {
            over.Box(0, 12, 0, 8, 12, 4, 16, 32, 0.25);
            over.Box(-4 - aw / 2, 12, 0, aw, 12, 4, 40, 32, 0.25);
            over.Box(4 + aw / 2, 12, 0, aw, 12, 4, 48, 48, 0.25);
            over.Box(-2, 0, 0, 4, 12, 4, 0, 32, 0.25);
            over.Box(2, 0, 0, 4, 12, 4, 0, 48, 0.25);
        }

        var group = new Model3DGroup();
        // свет стоит в самой сцене (не крутится вместе с моделью), поэтому спина освещена так же, как лицо
        group.Children.Add(new GeometryModel3D(body.G, mat));

        if (cape != null)
        {
            // развёртка плаща рассчитана на 64x32; у плащей высокого разрешения масштаб тот же
            var cm = new Mesh(64, 32);
            cm.Box(0, -16, 0, 10, 16, 1, 0, 0);
            var t = new Transform3DGroup();
            t.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 180)));   // рисунок - наружу, за спину
            t.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 9)));     // лёгкий отлёт снизу
            t.Children.Add(new TranslateTransform3D(0, 24, -2.6));
            var cmat = Mat(cape);
            group.Children.Add(new GeometryModel3D(cm.G, cmat) { BackMaterial = cmat, Transform = t });
        }

        // полупрозрачный второй слой - последним, иначе он закроет то, что под ним
        group.Children.Add(new GeometryModel3D(over.G, mat) { BackMaterial = mat });
        return group;
    }
}
