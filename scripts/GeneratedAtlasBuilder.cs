using Godot;
using System;

public static class GeneratedAtlasBuilder
{
    // Simple procedural atlas for planet surface. Iso-diamond tiles with subtle noise and shading.
    private static int _cachedSourceId = -1;

    public static int EnsureFloorSource(TileMapLayer floors, int currentSourceId)
    {
        if (floors == null || floors.TileSet == null) return currentSourceId;
        if (_cachedSourceId >= 0) return _cachedSourceId;

        int tileW = 64, tileH = 32;
        // Supersampling scale for source authoring (render at 3x and downscale into atlas 64x32)
        int superScale = 3;
        int hiW = tileW * superScale;
        int hiH = tileH * superScale;

        // Expand atlas width to host base tiles + 12 grass Wang variants (4x3)
        // 0..11 base/system tiles, [12..23] grass Wang variants
        int cols = 32, rows = 1;
        int imgW = cols * tileW;
        int imgH = rows * tileH;

        Image img = Image.Create(imgW, imgH, false, Image.Format.Rgba8);
        img.Fill(new Color(0,0,0,0));

        DrawIso(img, 0,           0, tileW, tileH, new Color(0.35f, 0.58f, 0.34f)); // grass (чуть светлее)
        DrawIso(img, tileW*1,     0, tileW, tileH, new Color(0.82f, 0.75f, 0.45f)); // sand
        DrawIso(img, tileW*2,     0, tileW, tileH, new Color(0.90f, 0.93f, 0.96f)); // snow
        DrawIso(img, tileW*3,     0, tileW, tileH, new Color(0.50f, 0.52f, 0.56f)); // stone
        DrawIso(img, tileW*4,     0, tileW, tileH, new Color(0.10f, 0.42f, 0.78f)); // water richer
        DrawIso(img, tileW*5,     0, tileW, tileH, new Color(0.80f, 0.92f, 0.97f)); // ice brighter
        DrawBridge(img, tileW*6,  0, tileW, tileH, true);  // natural bridge horizontal
        DrawBridge(img, tileW*7,  0, tileW, tileH, false); // natural bridge vertical

        // 8..11 зарезервированы (ранее использовались как variants)
        DrawIso(img, tileW*8,     0, tileW, tileH, new Color(0.37f, 0.60f, 0.36f));
        DrawIso(img, tileW*9,     0, tileW, tileH, new Color(0.33f, 0.56f, 0.33f));
        DrawIso(img, tileW*10,    0, tileW, tileH, new Color(0.40f, 0.61f, 0.38f));
        DrawIso(img, tileW*11,    0, tileW, tileH, new Color(0.31f, 0.54f, 0.31f));

        // Подмешаем внешний PNG с Wang-набором травы 4x3 в высоком разрешении (если отсутствует — создадим)
        string wangPath = "res://resources/textures/grass_wang.png";
        EnsureGrassWangPng(wangPath, hiW, hiH); // создаём 3x крупнее, затем даунскейл до 64x32
        var grassImg = Image.LoadFromFile(wangPath);
        if (grassImg != null && grassImg.GetWidth() >= hiW*4 && grassImg.GetHeight() >= hiH*3)
        {
            int outIndex = 12; // куда раскладываем в атласе
            for (int gy = 0; gy < 3; gy++)
            for (int gx = 0; gx < 4; gx++)
            {
                var hiRegion = grassImg.GetRegion(new Rect2I(gx*hiW, gy*hiH, hiW, hiH));
                // Даунскейл до 64x32 с высоким качеством
                hiRegion.Resize(tileW, tileH, Image.Interpolation.Lanczos);
                img.BlitRect(hiRegion, new Rect2I(0,0,tileW,tileH), new Vector2I(tileW * outIndex, 0));
                outIndex++;
            }
        }

        // Register atlas source into TileSet and return id
        var ts = floors.TileSet;
        var tex = ImageTexture.CreateFromImage(img);
        var atlas = new TileSetAtlasSource();
        atlas.Texture = tex;
        atlas.TextureRegionSize = new Vector2I(tileW, tileH);
        atlas.UseTexturePadding = true;

        // Create tiles for all atlas coordinates we use
        for (int x = 0; x < cols; x++)
        {
            atlas.CreateTile(new Vector2I(x, 0));
        }

        int newId = ts.AddSource(atlas);
        _cachedSourceId = newId;
        return _cachedSourceId;
    }

    private static void EnsureGrassWangPng(string path, int hiTileW, int hiTileH)
    {
        if (FileAccess.FileExists(path)) return;
        var dir = DirAccess.Open("res://resources/textures");
        if (dir == null) return; // если нет прав/пути — пропускаем

        // Храним 4x3 вариантов в высоком разрешении (каждый тайл = 3x 64x32 => 192x96)
        int cols = 4, rows = 3; int w = cols*hiTileW, h = rows*hiTileH;
        var img = Image.Create(w, h, false, Image.Format.Rgba8);
        img.Fill(new Color(0,0,0,0));

        // палитра травы
        var bases = new Color[]{ new Color(0.33f,0.58f,0.35f), new Color(0.36f,0.61f,0.37f), new Color(0.30f,0.55f,0.33f), new Color(0.38f,0.62f,0.39f),
                                 new Color(0.35f,0.60f,0.36f), new Color(0.34f,0.57f,0.35f), new Color(0.32f,0.56f,0.34f), new Color(0.37f,0.63f,0.40f)};
        int idx = 0;
        for (int gy=0; gy<rows; gy++)
        for (int gx=0; gx<cols; gx++)
        {
            DrawGrassTile(img, gx*hiTileW, gy*hiTileH, hiTileW, hiTileH, bases[idx++ % bases.Length]);
        }
        img.SavePng(path);
    }

    private static void DrawGrassTile(Image img, int ox, int oy, int w, int h, Color baseCol)
    {
        // изо-ромб с процедурной травяной текстурой (бесшовной): смесь косинусных волн и «прожилок»
        int cx = ox + w/2; int cy = oy + h/2;
        float halfW = w*0.5f; float halfH = h*0.5f;
        var rng = new Random(ox*2654435761u.GetHashCode() ^ oy*40503);
        for (int y=oy; y<oy+h; y++)
        for (int x=ox; x<ox+w; x++)
        {
            float dx = Math.Abs(x - cx)/halfW; float dy = Math.Abs(y - cy)/halfH;
            if (dx + dy > 1.0f) continue;
            float u = (x-ox)/(float)w; float v=(y-oy)/(float)h;
            float tex = 0.18f*(MathF.Cos(2*MathF.PI*3*u)*MathF.Cos(2*MathF.PI*2.2f*v))
                      + 0.14f*(MathF.Cos(2*MathF.PI*1.1f*(u+v)))
                      + 0.08f*(MathF.Cos(2*MathF.PI*4.1f*(u*0.6f+v*0.4f)));
            float vein = 0.06f*MathF.Sin(2*MathF.PI*(u*5.3f + v*5.7f));
            float k = MathF.Max(0f, 1f-(dx+dy)); // плавное затухание к краям ромба
            float rnd = (float)(rng.NextDouble()*2-1) * 0.018f * k*k;
            var c = new Color(
                Math.Clamp(baseCol.R + tex + vein + rnd, 0,1),
                Math.Clamp(baseCol.G + tex + vein + rnd, 0,1),
                Math.Clamp(baseCol.B + tex + vein + rnd, 0,1), 1);
            img.SetPixel(x,y,c);
        }
    }

    private static void DrawIso(Image img, int ox, int oy, int w, int h, Color baseCol)
    {
        int cx = ox + w/2;
        int cy = oy + h/2;
        float halfW = w * 0.5f - 1;
        float halfH = h * 0.5f - 1;
        var rng = new Random(ox*73856093 ^ oy*19349663);
        // Periodic texture basis (tileable): combination of cosines
        float freqU = 4f, freqV = 3f;
        for (int y = oy; y < oy + h; y++)
        {
            for (int x = ox; x < ox + w; x++)
            {
                float dx = Math.Abs(x - cx) / halfW;
                float dy = Math.Abs(y - cy) / halfH;
                if (dx + dy <= 1.0f)
                {
                    // u,v в [0,1] внутри изо-ромба для создания плиточной текстуры без швов
                    float u = (x - ox) / (float)w;
                    float v = (y - oy) / (float)h;
                    // периодическая «зернистость» без швов (косинусы обнуляются на краях)
                    float tex = 0.25f * (MathF.Cos(2*MathF.PI*freqU*u) * MathF.Cos(2*MathF.PI*freqV*v))
                               + 0.15f * (MathF.Cos(2*MathF.PI*(freqU*0.5f)*(u+v)));
                    // плавно затухающий случайный шум, у краёв = 0
                    float k = MathF.Max(0f, 1f - (dx + dy));
                    float noiseAmp = 0.03f * k * k;
                    float rnd = (float)(rng.NextDouble()*2 - 1) * noiseAmp;
                    float shade = 0f;
                    var c = new Color(
                        Math.Clamp(baseCol.R + tex + rnd - shade, 0, 1),
                        Math.Clamp(baseCol.G + tex + rnd - shade, 0, 1),
                        Math.Clamp(baseCol.B + tex + rnd - shade, 0, 1),
                        1
                    );
                    img.SetPixel(x, y, c);
                }
            }
        }
    }

    private static void DrawBridge(Image img, int ox, int oy, int w, int h, bool horizontal)
    {
        // base: natural ground color as substrate for land bridge
        DrawIso(img, ox, oy, w, h, new Color(0.42f, 0.40f, 0.30f));
        int cx = ox + w/2; int cy = oy + h/2;
        int len = horizontal ? w : h;
        for (int i = -len/3; i <= len/3; i += (horizontal ? 8 : 8))
        {
            for (int t = -3; t <= 3; t++)
            {
                int px = horizontal ? cx + i : cx + t;
                int py = horizontal ? cy + t : cy + i;
                // set stone color if inside
                if (px >= ox && px < ox + w && py >= oy && py < oy + h)
                {
                    img.SetPixel(px, py, new Color(0.36f, 0.36f, 0.36f, 1));
                }
            }
        }
    }
}


