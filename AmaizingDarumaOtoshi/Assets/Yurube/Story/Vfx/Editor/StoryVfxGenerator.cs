#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Yurube.Story.Vfx.EditorTools
{
    // ストーリーカットシーン用エフェクト（テクスチャPNG・マテリアル・プレハブ・カタログ・プレビューシーン）を一括生成するエディタツール。
    //  ・Yurube/ストーリーエフェクト/一括生成（既存は上書きしない）: 未生成アセットのみ作成（人間が手調整した既存プレハブは上書きしない）
    //  ・Yurube/ストーリーエフェクト/一括再生成（すべて上書き）      : 確認のうえ全アセットを上書き再生成
    //  ・Yurube/ストーリーエフェクト/プレビューシーンを生成して開く  : プレビューシーン StoryVfxPreview.unity を生成して開く
    public static class StoryVfxGenerator
    {
        const string RootDir = "Assets/Yurube/Story/Vfx";
        const string TexDir = RootDir + "/Textures";
        const string MatDir = RootDir + "/Materials";
        const string PrefabDir = RootDir + "/Prefabs";
        const string CatalogPath = RootDir + "/StoryVfxCatalog.asset";
        const string PreviewScenePath = RootDir + "/StoryVfxPreview.unity";
        const string StorySettingsPath = "Assets/Yurube/Story/Resources/StorySettings.asset";

        const string ShaderAlpha = "Yurube/StoryVfxAlpha";
        const string ShaderAdditive = "Yurube/StoryVfxAdditive";
        const string ShaderParticlesLit = "Universal Render Pipeline/Particles/Lit";
        const string ShaderParticlesUnlit = "Universal Render Pipeline/Particles/Unlit";

        static Mesh s_cachedCubeMesh;

        [MenuItem("Yurube/ストーリーエフェクト/一括生成（既存は上書きしない）", false, 100)]
        public static void GenerateAll()
        {
            RunGeneration(overwriteExisting: false);
        }

        [MenuItem("Yurube/ストーリーエフェクト/一括再生成（すべて上書き）", false, 101)]
        public static void RegenerateAllOverwrite()
        {
            bool ok = EditorUtility.DisplayDialog(
                "ストーリーエフェクト 再生成（上書き）",
                "既存のテクスチャ・マテリアル・プレハブ・プレビューシーンをすべて上書き再生成します。\nインスペクターで手調整したプレハブの変更は失われます。続行しますか？",
                "上書き再生成する",
                "キャンセル");
            if (!ok) return;
            RunGeneration(overwriteExisting: true);
        }

        [MenuItem("Yurube/ストーリーエフェクト/プレビューシーンを生成して開く", false, 120)]
        public static void GeneratePreviewSceneMenu()
        {
            EnsureFolders();
            var catalog = AssetDatabase.LoadAssetAtPath<StoryVfxCatalog>(CatalogPath);
            if (catalog == null)
            {
                RunGeneration(overwriteExisting: false);
                catalog = AssetDatabase.LoadAssetAtPath<StoryVfxCatalog>(CatalogPath);
            }
            BuildPreviewScene(catalog, overwrite: true, openAfter: true);
        }

        static void RunGeneration(bool overwriteExisting)
        {
            EnsureFolders();

            int createdCount = 0;
            int skippedCount = 0;

            // 1. テクスチャ（PNG）生成
            var texSoftDot = EnsureTexture(TexDir + "/vfx_soft_dot.png", CreateSoftDotPixels, 128, 128, false, overwriteExisting, ref createdCount, ref skippedCount);
            var texSmoke = EnsureTexture(TexDir + "/vfx_smoke_puff.png", CreateSmokePuffPixels, 128, 128, false, overwriteExisting, ref createdCount, ref skippedCount);
            var texRing = EnsureTexture(TexDir + "/vfx_ring.png", CreateRingPixels, 128, 128, false, overwriteExisting, ref createdCount, ref skippedCount);
            var texSpark = EnsureTexture(TexDir + "/vfx_spark_streak.png", CreateSparkStreakPixels, 128, 64, false, overwriteExisting, ref createdCount, ref skippedCount);
            var texStar = EnsureTexture(TexDir + "/vfx_star_4.png", CreateStar4Pixels, 128, 128, false, overwriteExisting, ref createdCount, ref skippedCount);
            var texConfetti = EnsureTexture(TexDir + "/vfx_confetti_card.png", CreateConfettiPixels, 64, 64, false, overwriteExisting, ref createdCount, ref skippedCount);
            EnsureTexture(TexDir + "/vfx_vignette.png", CreateVignettePixels, 256, 256, true, overwriteExisting, ref createdCount, ref skippedCount);

            AssetDatabase.Refresh();

            // 2. マテリアル生成（すべて URP 互換・ピンク防止）
            var matCubeLit = EnsureParticlesLitMaterial(MatDir + "/mat_vfx_cube_lit.mat", 0.05f, 0.35f, overwriteExisting, ref createdCount, ref skippedCount);
            var matGoldCubeLit = EnsureParticlesLitMaterial(MatDir + "/mat_vfx_gold_cube_lit.mat", 0.72f, 0.78f, overwriteExisting, ref createdCount, ref skippedCount);
            var matDustAlpha = EnsureCustomParticleMaterial(MatDir + "/mat_vfx_dust_alpha.mat", ShaderAlpha, texSmoke, 1f, overwriteExisting, ref createdCount, ref skippedCount);
            var matSoftAlpha = EnsureCustomParticleMaterial(MatDir + "/mat_vfx_soft_alpha.mat", ShaderAlpha, texSoftDot, 1f, overwriteExisting, ref createdCount, ref skippedCount);
            var matConfettiAlpha = EnsureCustomParticleMaterial(MatDir + "/mat_vfx_confetti_alpha.mat", ShaderAlpha, texConfetti, 1f, overwriteExisting, ref createdCount, ref skippedCount);
            var matRingAdd = EnsureCustomParticleMaterial(MatDir + "/mat_vfx_ring_add.mat", ShaderAdditive, texRing, 2.0f, overwriteExisting, ref createdCount, ref skippedCount);
            var matSparkAdd = EnsureCustomParticleMaterial(MatDir + "/mat_vfx_spark_add.mat", ShaderAdditive, texSpark, 2.2f, overwriteExisting, ref createdCount, ref skippedCount);
            var matGlowAdd = EnsureCustomParticleMaterial(MatDir + "/mat_vfx_glow_add.mat", ShaderAdditive, texSoftDot, 1.9f, overwriteExisting, ref createdCount, ref skippedCount);
            var matStarAdd = EnsureCustomParticleMaterial(MatDir + "/mat_vfx_star_add.mat", ShaderAdditive, texStar, 2.2f, overwriteExisting, ref createdCount, ref skippedCount);

            var mats = new MaterialSet
            {
                CubeLit = matCubeLit,
                GoldCubeLit = matGoldCubeLit,
                DustAlpha = matDustAlpha,
                SoftAlpha = matSoftAlpha,
                ConfettiAlpha = matConfettiAlpha,
                RingAdd = matRingAdd,
                SparkAdd = matSparkAdd,
                GlowAdd = matGlowAdd,
                StarAdd = matStarAdd,
            };

            // 3. プレハブ生成（P1 → P2 → P3）
            var entries = new List<StoryVfxEntry>
            {
                // ---- P1: 画面効果 ----
                new StoryVfxEntry
                {
                    key = "fx_fade",
                    label = "暗転・明転（プロローグ5・第3章告白・最終章場面転換）",
                    priority = StoryVfxPriority.P1,
                    category = StoryVfxCategory.ScreenOverlay,
                    defaultTint = Color.black,
                    useUnscaledTime = true,
                },
                new StoryVfxEntry
                {
                    key = "fx_vignette",
                    label = "視界周縁が一瞬暗くなる（序章）",
                    priority = StoryVfxPriority.P1,
                    category = StoryVfxCategory.ScreenOverlay,
                    defaultTint = new Color(0.03f, 0.02f, 0.06f, 1f),
                    useUnscaledTime = true,
                },

                // ---- P1: ワールド空間 ----
                CreateEntry("fx_block_collapse_s", "2〜3個の積み木崩壊＋少量の砂埃（プロローグ3）",
                    StoryVfxPriority.P1, StoryVfxCategory.World, false, false,
                    EnsurePrefab("fx_block_collapse_s", false, root => BuildBlockCollapseSmall(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),

                CreateEntry("fx_dust_puff_small", "床の小さな埃がふわっと上がって消える（プロローグ7）",
                    StoryVfxPriority.P1, StoryVfxCategory.World, false, false,
                    EnsurePrefab("fx_dust_puff_small", false, root => BuildDustPuffSmall(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),

                CreateEntry("fx_confetti", "七色の紙吹雪が上から降る（第4章優勝・最終章開幕）",
                    StoryVfxPriority.P1, StoryVfxCategory.World, true, false,
                    EnsurePrefab("fx_confetti", true, root => BuildConfetti(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),

                // ---- P2: ワールド空間・UI前面 ----
                CreateEntry("fx_block_collapse_l", "大きな塔・舞台の崩壊（多数の欠片＋大きな砂埃）",
                    StoryVfxPriority.P2, StoryVfxCategory.World, false, false,
                    EnsurePrefab("fx_block_collapse_l", false, root => BuildBlockCollapseLarge(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),

                CreateEntry("fx_throne_shatter", "王座と盾が砕ける（金色金属・石欠片＋埃＋光の粒）",
                    StoryVfxPriority.P2, StoryVfxCategory.World, false, false,
                    EnsurePrefab("fx_throne_shatter", false, root => BuildThroneShatter(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),

                CreateEntry("fx_parry_impact", "パリィ衝撃（広がるリング＋火花＋短い中心光）",
                    StoryVfxPriority.P2, StoryVfxCategory.World, false, false,
                    EnsurePrefab("fx_parry_impact", false, root => BuildParryImpact(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),

                CreateEntry("fx_sword_trail", "王の剣投擲の追従軌跡（金白トレイル＋光の粒）",
                    StoryVfxPriority.P2, StoryVfxCategory.World, true, true,
                    EnsurePrefab("fx_sword_trail", true, root => BuildSwordTrail(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),

                CreateEntry("fx_darkness_aura", "闇落ちオーラ（黒紫のもや＋暗い粒子・強さ可変・抽象表現）",
                    StoryVfxPriority.P2, StoryVfxCategory.World, true, false,
                    EnsurePrefab("fx_darkness_aura", true, root => BuildDarknessAura(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),

                CreateEntry("fx_shard_swirl", "七色の積み木欠片が対象の周囲を螺旋状に旋回",
                    StoryVfxPriority.P2, StoryVfxCategory.World, true, false,
                    EnsurePrefab("fx_shard_swirl", true, root => BuildShardSwirl(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),

                CreateEntry("fx_arena_collapse", "闘技場の積み木が広範囲で時間差崩落＋暗い煙",
                    StoryVfxPriority.P2, StoryVfxCategory.World, false, false,
                    EnsurePrefab("fx_arena_collapse", false, root => BuildArenaCollapse(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),

                CreateEntry("fx_sparkle_burst", "金色のきらめき（だるきんぐ解放通知まわり・UI前面）",
                    StoryVfxPriority.P2, StoryVfxCategory.ScreenFront, false, false,
                    EnsurePrefab("fx_sparkle_burst", false, root => BuildSparkleBurst(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),

                // ---- P3: 画面効果・環境演出 ----
                new StoryVfxEntry
                {
                    key = "fx_flash",
                    label = "パリィ衝撃の単発白閃光（0.12秒以上フェード・連発防止）",
                    priority = StoryVfxPriority.P3,
                    category = StoryVfxCategory.ScreenOverlay,
                    defaultTint = new Color(1f, 0.98f, 0.92f, 1f),
                    useUnscaledTime = true,
                },

                CreateEntry("fx_dust_motes", "光の中をゆっくり舞うごく弱い埃（序章・第3章部室）",
                    StoryVfxPriority.P3, StoryVfxCategory.World, true, false,
                    EnsurePrefab("fx_dust_motes", true, root => BuildDustMotes(root, mats), overwriteExisting, ref createdCount, ref skippedCount)),
            };

            // 4. カタログ生成・更新
            var catalog = EnsureCatalog(entries, overwriteExisting);

            // 5. StorySettings.asset へカタログを自動紐づけ（Resources を増やさない）
            BindCatalogToStorySettings(catalog);

            // 6. プレビューシーン生成（既存なら overwrite 時のみ再生成）
            if (overwriteExisting || !File.Exists(PreviewScenePath))
            {
                BuildPreviewScene(catalog, overwrite: true, openAfter: false);
                createdCount++;
            }
            else
            {
                skippedCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[StoryVfxGenerator] 完了: 新規/更新={createdCount}, 既存スキップ={skippedCount} ({RootDir})");
        }

        static StoryVfxEntry CreateEntry(string key, string label, StoryVfxPriority priority, StoryVfxCategory category,
                                         bool looping, bool previewOrbit, GameObject prefab)
        {
            return new StoryVfxEntry
            {
                key = key,
                label = label,
                priority = priority,
                category = category,
                prefab = prefab,
                looping = looping,
                defaultScale = 1f,
                defaultTint = Color.white,
                useUnscaledTime = true,
                previewOrbitMotion = previewOrbit,
            };
        }

        //========================================
        // フォルダ・カタログ・StorySettings 紐づけ
        //========================================

        static void EnsureFolders()
        {
            CreateFolderIfMissing("Assets/Yurube/Story", "Vfx");
            CreateFolderIfMissing(RootDir, "Textures");
            CreateFolderIfMissing(RootDir, "Materials");
            CreateFolderIfMissing(RootDir, "Prefabs");
        }

        static void CreateFolderIfMissing(string parent, string name)
        {
            string full = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(full))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        static StoryVfxCatalog EnsureCatalog(List<StoryVfxEntry> generatedEntries, bool overwrite)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<StoryVfxCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<StoryVfxCatalog>();
                catalog.SetEntries(generatedEntries);
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            else if (overwrite)
            {
                catalog.SetEntries(generatedEntries);
                EditorUtility.SetDirty(catalog);
            }
            else
            {
                // 既存の調整値を壊さず、未登録キー・未接続プレハブのみ補完する
                for (int i = 0; i < generatedEntries.Count; i++)
                {
                    var gen = generatedEntries[i];
                    var existing = catalog.Find(gen.key);
                    if (existing == null)
                    {
                        catalog.UpsertEntry(gen);
                    }
                    else if (existing.prefab == null && gen.prefab != null)
                    {
                        existing.prefab = gen.prefab;
                    }
                }
                EditorUtility.SetDirty(catalog);
            }
            return catalog;
        }

        static void BindCatalogToStorySettings(StoryVfxCatalog catalog)
        {
            if (catalog == null) return;
            var settings = AssetDatabase.LoadAssetAtPath<StorySettings>(StorySettingsPath);
            if (settings == null) return;

            var so = new SerializedObject(settings);
            var prop = so.FindProperty("m_vfxCatalog");
            if (prop != null && prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = catalog;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
        }

        //========================================
        // テクスチャ手続き生成（PNG 出力）
        //========================================

        static Texture2D EnsureTexture(string assetPath, Func<int, int, Color[]> pixelBuilder, int width, int height,
                                       bool asSprite, bool overwrite, ref int created, ref int skipped)
        {
            bool exists = File.Exists(assetPath);
            if (!exists || overwrite)
            {
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.SetPixels(pixelBuilder(width, height));
                tex.Apply();
                byte[] png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);

                File.WriteAllBytes(assetPath, png);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = asSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
                created++;
            }
            else
            {
                skipped++;
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        static Color[] CreateSoftDotPixels(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w * 2f - 1f;
                    float ny = (y + 0.5f) / h * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return px;
        }

        static Color[] CreateSmokePuffPixels(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w * 2f - 1f;
                    float ny = (y + 0.5f) / h * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float n1 = Mathf.PerlinNoise(nx * 2.6f + 11.3f, ny * 2.6f + 7.1f);
                    float n2 = Mathf.PerlinNoise(nx * 5.2f + 3.7f, ny * 5.2f + 19.4f);
                    float puff = n1 * 0.7f + n2 * 0.3f;
                    float radial = Mathf.Clamp01(1f - d * (0.95f + (0.5f - puff) * 0.35f));
                    float a = Mathf.Pow(radial, 1.55f) * Mathf.Lerp(0.65f, 1f, puff);
                    float shade = Mathf.Lerp(0.84f, 1f, puff);
                    px[y * w + x] = new Color(shade, shade, shade, Mathf.Clamp01(a));
                }
            }
            return px;
        }

        static Color[] CreateRingPixels(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w * 2f - 1f;
                    float ny = (y + 0.5f) / h * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float band = Mathf.Clamp01(1f - Mathf.Abs(d - 0.80f) / 0.14f);
                    float outerGlow = Mathf.Clamp01(1f - Mathf.Abs(d - 0.78f) / 0.24f) * 0.35f;
                    float a = Mathf.Clamp01(band * band + outerGlow);
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return px;
        }

        static Color[] CreateSparkStreakPixels(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float ny = (y + 0.5f) / h * 2f - 1f;
                    // 横方向に先端が鋭く、尾へ柔らかく消える光の筋
                    float across = Mathf.Exp(-ny * ny * 7.5f);
                    float along = Mathf.Clamp01(u / 0.12f) * Mathf.Pow(Mathf.Clamp01((1f - u) / 0.88f), 0.75f);
                    float a = Mathf.Clamp01(across * along);
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return px;
        }

        static Color[] CreateStar4Pixels(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                    float ny = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float core = Mathf.Exp(-d * d * 14f);
                    float rayH = Mathf.Exp(-ny * ny * 110f) * Mathf.Clamp01(1f - nx);
                    float rayV = Mathf.Exp(-nx * nx * 110f) * Mathf.Clamp01(1f - ny);
                    float a = Mathf.Clamp01(core + (rayH + rayV) * 0.95f) * Mathf.Clamp01((1f - d) * 4f);
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return px;
        }

        static Color[] CreateConfettiPixels(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                    float ny = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                    // 長方形の紙片（横幅やや細め）
                    float dx = Mathf.Max(0f, nx - 0.52f);
                    float dy = Mathf.Max(0f, ny - 0.80f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - dist / 0.10f);
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return px;
        }

        static Color[] CreateVignettePixels(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w * 2f - 1f;
                    float ny = (y + 0.5f) / h * 2f - 1f;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 1.12f, d));
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            return px;
        }

        //========================================
        // URP マテリアル生成
        //========================================

        class MaterialSet
        {
            public Material CubeLit;
            public Material GoldCubeLit;
            public Material DustAlpha;
            public Material SoftAlpha;
            public Material ConfettiAlpha;
            public Material RingAdd;
            public Material SparkAdd;
            public Material GlowAdd;
            public Material StarAdd;
        }

        static Material EnsureParticlesLitMaterial(string path, float metallic, float smoothness,
                                                   bool overwrite, ref int created, ref int skipped)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && !overwrite)
            {
                skipped++;
                return existing;
            }

            Shader shader = Shader.Find(ShaderParticlesLit);
            if (shader == null) shader = Shader.Find(ShaderParticlesUnlit);
            if (shader == null) shader = Shader.Find(ShaderAlpha);

            var mat = existing != null ? existing : new Material(shader);
            mat.shader = shader;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f); // Opaque
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", 0f); // Multiply with vertex color
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Geometry;

            if (existing == null) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            created++;
            return mat;
        }

        static Material EnsureCustomParticleMaterial(string path, string shaderName, Texture2D tex, float intensity,
                                                     bool overwrite, ref int created, ref int skipped)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && !overwrite)
            {
                skipped++;
                return existing;
            }

            Shader shader = Shader.Find(shaderName);
            if (shader == null) shader = Shader.Find(ShaderParticlesUnlit);
            if (shader == null) shader = Shader.Find("Sprites/Default");

            var mat = existing != null ? existing : new Material(shader);
            mat.shader = shader;
            if (tex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else if (tex != null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Intensity")) mat.SetFloat("_Intensity", intensity);

            if (existing == null) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            created++;
            return mat;
        }

        //========================================
        // プレハブ生成共通フレーム
        //========================================

        static GameObject EnsurePrefab(string key, bool looping, Action<GameObject> builder,
                                       bool overwrite, ref int created, ref int skipped)
        {
            string path = $"{PrefabDir}/{key}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && !overwrite)
            {
                skipped++;
                return existing;
            }

            var previewScene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject(key);
            SceneManager.MoveGameObjectToScene(root, previewScene);
            try
            {
                var inst = root.AddComponent<StoryVfxInstance>();
                inst.ConfigureDefaults(key, looping, useUnscaledTime: true, defaultScale: 1f);
                builder(root);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
                created++;
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        static Mesh GetCubeMesh()
        {
            if (s_cachedCubeMesh != null) return s_cachedCubeMesh;
            s_cachedCubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (s_cachedCubeMesh == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s_cachedCubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                UnityEngine.Object.DestroyImmediate(tmp);
            }
            return s_cachedCubeMesh;
        }

        static ParticleSystem CreatePS(string name, Transform parent, Material mat, float duration, bool loop, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = duration;
            main.loop = loop;
            main.playOnAwake = false;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.useUnscaledTime = true;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        static void ConfigureCubeMeshRenderer(ParticleSystem ps, Material mat)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = GetCubeMesh();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        static void SetFadeAlpha(ParticleSystem ps, float fadeInEnd, float fadeOutStart, Color? startTint = null, Color? endTint = null)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            Color c0 = startTint ?? Color.white;
            Color c1 = endTint ?? Color.white;
            g.SetKeys(
                new[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 1f) },
                new[]
                {
                    new GradientAlphaKey(fadeInEnd > 0.001f ? 0f : 1f, 0f),
                    new GradientAlphaKey(1f, Mathf.Clamp(fadeInEnd, 0.01f, 0.45f)),
                    new GradientAlphaKey(1f, Mathf.Clamp(fadeOutStart, 0.5f, 0.95f)),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        static void SetSizeCurve(ParticleSystem ps, AnimationCurve curve)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        static void Enable3DRotation(ParticleSystem ps, float degPerSecMin, float degPerSecMax)
        {
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            float rMin = degPerSecMin * Mathf.Deg2Rad;
            float rMax = degPerSecMax * Mathf.Deg2Rad;
            rot.x = new ParticleSystem.MinMaxCurve(rMin, rMax);
            rot.y = new ParticleSystem.MinMaxCurve(rMin, rMax);
            rot.z = new ParticleSystem.MinMaxCurve(rMin, rMax);
        }

        //========================================
        // P1 プレハブ構築
        //========================================

        // P1: fx_block_collapse_s（2〜3個の積み木欠片＋少量の砂埃）
        static void BuildBlockCollapseSmall(GameObject root, MaterialSet mats)
        {
            var blocks = CreatePS("Blocks", root.transform, mats.CubeLit, 1.4f, false, 16);
            var bMain = blocks.main;
            bMain.startLifetime = new ParticleSystem.MinMaxCurve(0.85f, 1.25f);
            bMain.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            bMain.startSize3D = true;
            bMain.startSizeX = new ParticleSystem.MinMaxCurve(0.32f, 0.44f);
            bMain.startSizeY = new ParticleSystem.MinMaxCurve(0.24f, 0.34f);
            bMain.startSizeZ = new ParticleSystem.MinMaxCurve(0.32f, 0.44f);
            bMain.startRotation3D = true;
            bMain.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bMain.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.BuildFixedGradient(StoryVfxPalette.TumikiColors))
            {
                mode = ParticleSystemGradientMode.RandomColor
            };
            bMain.gravityModifier = 1.85f;
            blocks.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });

            var bShape = blocks.shape;
            bShape.enabled = true;
            bShape.shapeType = ParticleSystemShapeType.Hemisphere;
            bShape.radius = 0.28f;
            bShape.rotation = new Vector3(-90f, 0f, 0f);

            Enable3DRotation(blocks, -320f, 320f);
            SetSizeCurve(blocks, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.72f, 1f), new Keyframe(1f, 0f)));
            ConfigureCubeMeshRenderer(blocks, mats.CubeLit);

            // 少量の砂埃
            var dust = CreatePS("Dust", root.transform, mats.DustAlpha, 1.2f, false, 32);
            dust.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            var dMain = dust.main;
            dMain.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.05f);
            dMain.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1.35f);
            dMain.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            dMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            dMain.startColor = StoryVfxPalette.DustWarm;
            dMain.gravityModifier = -0.08f;
            dust.emission.SetBursts(new[] { new ParticleSystem.Burst(0.02f, 12) });

            var dShape = dust.shape;
            dShape.enabled = true;
            dShape.shapeType = ParticleSystemShapeType.Circle;
            dShape.radius = 0.35f;
            dShape.rotation = new Vector3(90f, 0f, 0f);

            var dLimit = dust.limitVelocityOverLifetime;
            dLimit.enabled = true;
            dLimit.limit = 0.15f;
            dLimit.dampen = 0.08f;

            SetSizeCurve(dust, AnimationCurve.EaseInOut(0f, 0.45f, 1f, 1.75f));
            SetFadeAlpha(dust, 0.12f, 0.45f);
        }

        // P1: fx_dust_puff_small（床を転がる槌が止まる際の小さな埃）
        static void BuildDustPuffSmall(GameObject root, MaterialSet mats)
        {
            var puff = CreatePS("Puff", root.transform, mats.DustAlpha, 1.1f, false, 32);
            var main = puff.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.30f, 0.62f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.80f, 0.76f, 0.70f, 0.52f);
            main.gravityModifier = -0.10f;
            puff.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 11) });

            var shape = puff.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.22f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var limit = puff.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.1f;
            limit.dampen = 0.1f;

            SetSizeCurve(puff, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.85f));
            SetFadeAlpha(puff, 0.15f, 0.45f);

            var specs = CreatePS("Specs", root.transform, mats.SoftAlpha, 1.0f, false, 16);
            var sMain = specs.main;
            sMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
            sMain.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            sMain.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            sMain.startColor = new Color(0.85f, 0.80f, 0.72f, 0.65f);
            sMain.gravityModifier = -0.05f;
            specs.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
            var sShape = specs.shape;
            sShape.enabled = true;
            sShape.shapeType = ParticleSystemShapeType.Hemisphere;
            sShape.radius = 0.25f;
            sShape.rotation = new Vector3(-90f, 0f, 0f);
            SetFadeAlpha(specs, 0.1f, 0.55f);
        }

        // P1: fx_confetti（七色の紙吹雪が上から降る）
        static void BuildConfetti(GameObject root, MaterialSet mats)
        {
            var ps = CreatePS("Confetti", root.transform, mats.ConfettiAlpha, 4.5f, true, 240);
            ps.transform.localPosition = new Vector3(0f, 3.8f, 0f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.0f, 4.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.BuildFixedGradient(StoryVfxPalette.SevenColors))
            {
                mode = ParticleSystemGradientMode.RandomColor
            };
            main.gravityModifier = 0.18f;

            var emission = ps.emission;
            emission.rateOverTime = 46f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(6.5f, 0.4f, 3.2f);

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1.8f;
            limit.dampen = 0.08f;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.85f;
            noise.frequency = 0.7f;
            noise.scrollSpeed = 0.45f;

            Enable3DRotation(ps, -260f, 260f);
            SetFadeAlpha(ps, 0.05f, 0.82f);
        }

        //========================================
        // P2 プレハブ構築
        //========================================

        // P2: fx_block_collapse_l（大きな塔・舞台の崩壊）
        static void BuildBlockCollapseLarge(GameObject root, MaterialSet mats)
        {
            var blocks = CreatePS("BlocksLarge", root.transform, mats.CubeLit, 2.0f, false, 120);
            var bMain = blocks.main;
            bMain.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            bMain.startSpeed = new ParticleSystem.MinMaxCurve(2.8f, 6.2f);
            bMain.startSize3D = true;
            bMain.startSizeX = new ParticleSystem.MinMaxCurve(0.28f, 0.54f);
            bMain.startSizeY = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
            bMain.startSizeZ = new ParticleSystem.MinMaxCurve(0.28f, 0.54f);
            bMain.startRotation3D = true;
            bMain.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bMain.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.BuildFixedGradient(StoryVfxPalette.TumikiColors))
            {
                mode = ParticleSystemGradientMode.RandomColor
            };
            bMain.gravityModifier = 2.0f;
            blocks.emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.0f, 30),
                new ParticleSystem.Burst(0.16f, 18),
            });

            var bShape = blocks.shape;
            bShape.enabled = true;
            bShape.shapeType = ParticleSystemShapeType.Box;
            bShape.scale = new Vector3(1.8f, 1.4f, 1.8f);

            Enable3DRotation(blocks, -420f, 420f);
            SetSizeCurve(blocks, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.75f, 0.95f), new Keyframe(1f, 0f)));
            ConfigureCubeMeshRenderer(blocks, mats.CubeLit);

            // 大きな砂埃
            var dust = CreatePS("DustCloud", root.transform, mats.DustAlpha, 1.9f, false, 64);
            var dMain = dust.main;
            dMain.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.7f);
            dMain.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.0f);
            dMain.startSize = new ParticleSystem.MinMaxCurve(1.1f, 2.2f);
            dMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            dMain.startColor = StoryVfxPalette.DustWarm;
            dMain.gravityModifier = -0.10f;
            dust.emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.0f, 22),
                new ParticleSystem.Burst(0.18f, 12),
            });

            var dShape = dust.shape;
            dShape.enabled = true;
            dShape.shapeType = ParticleSystemShapeType.Sphere;
            dShape.radius = 0.85f;

            var dLimit = dust.limitVelocityOverLifetime;
            dLimit.enabled = true;
            dLimit.limit = 0.25f;
            dLimit.dampen = 0.08f;

            SetSizeCurve(dust, AnimationCurve.EaseInOut(0f, 0.45f, 1f, 1.95f));
            SetFadeAlpha(dust, 0.1f, 0.45f);
        }

        // P2: fx_throne_shatter（王座と盾が砕ける：金色欠片＋石埃＋光の粒。流血表現なし）
        static void BuildThroneShatter(GameObject root, MaterialSet mats)
        {
            var shardColors = new[]
            {
                StoryVfxPalette.GoldBright,
                StoryVfxPalette.GoldDeep,
                StoryVfxPalette.GoldWhite,
                StoryVfxPalette.StoneGray,
            };

            var shards = CreatePS("GoldShards", root.transform, mats.GoldCubeLit, 1.8f, false, 96);
            var sMain = shards.main;
            sMain.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.65f);
            sMain.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 7.2f);
            sMain.startSize3D = true;
            sMain.startSizeX = new ParticleSystem.MinMaxCurve(0.18f, 0.44f);
            sMain.startSizeY = new ParticleSystem.MinMaxCurve(0.08f, 0.24f);
            sMain.startSizeZ = new ParticleSystem.MinMaxCurve(0.20f, 0.48f);
            sMain.startRotation3D = true;
            sMain.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sMain.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.BuildFixedGradient(shardColors))
            {
                mode = ParticleSystemGradientMode.RandomColor
            };
            sMain.gravityModifier = 1.9f;
            shards.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });

            var sShape = shards.shape;
            sShape.enabled = true;
            sShape.shapeType = ParticleSystemShapeType.Sphere;
            sShape.radius = 0.65f;

            Enable3DRotation(shards, -480f, 480f);
            SetSizeCurve(shards, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.76f, 0.9f), new Keyframe(1f, 0f)));
            ConfigureCubeMeshRenderer(shards, mats.GoldCubeLit);

            // 石埃
            var dust = CreatePS("StoneDust", root.transform, mats.DustAlpha, 1.6f, false, 48);
            var dMain = dust.main;
            dMain.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.45f);
            dMain.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 2.6f);
            dMain.startSize = new ParticleSystem.MinMaxCurve(0.85f, 1.75f);
            dMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            dMain.startColor = new Color(0.74f, 0.72f, 0.68f, 0.58f);
            dMain.gravityModifier = -0.06f;
            dust.emission.SetBursts(new[] { new ParticleSystem.Burst(0.02f, 22) });
            var dShape = dust.shape;
            dShape.enabled = true;
            dShape.shapeType = ParticleSystemShapeType.Sphere;
            dShape.radius = 0.55f;
            SetSizeCurve(dust, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.85f));
            SetFadeAlpha(dust, 0.1f, 0.45f);

            // 金白色の光の粒
            var motes = CreatePS("LightMotes", root.transform, mats.StarAdd, 1.7f, false, 64);
            var mMain = motes.main;
            mMain.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, 1.45f);
            mMain.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.4f);
            mMain.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.36f);
            mMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.GoldWhite, StoryVfxPalette.GoldBright);
            mMain.gravityModifier = -0.15f;
            motes.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });
            var mShape = motes.shape;
            mShape.enabled = true;
            mShape.shapeType = ParticleSystemShapeType.Sphere;
            mShape.radius = 0.5f;
            var mLimit = motes.limitVelocityOverLifetime;
            mLimit.enabled = true;
            mLimit.limit = 0.4f;
            mLimit.dampen = 0.1f;
            SetSizeCurve(motes, new AnimationCurve(
                new Keyframe(0f, 0.2f), new Keyframe(0.2f, 1f), new Keyframe(0.6f, 0.65f), new Keyframe(1f, 0f)));
            SetFadeAlpha(motes, 0.05f, 0.55f);
        }

        // P2: fx_parry_impact（衝撃リング＋火花＋一瞬の光。unscaledTime でヒットストップ対応）
        static void BuildParryImpact(GameObject root, MaterialSet mats)
        {
            // 衝撃リング（カメラ向き＋水平の2枚）
            var ring = CreatePS("ShockRing", root.transform, mats.RingAdd, 0.45f, false, 8);
            var rMain = ring.main;
            rMain.startLifetime = 0.36f;
            rMain.startSpeed = 0f;
            rMain.startSize = 3.2f;
            rMain.startColor = StoryVfxPalette.GoldWhite;
            ring.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            SetSizeCurve(ring, AnimationCurve.EaseInOut(0f, 0.12f, 1f, 1.25f));
            SetFadeAlpha(ring, 0.02f, 0.42f, StoryVfxPalette.GoldWhite, StoryVfxPalette.GoldBright);

            var ringFlat = CreatePS("ShockRingFlat", root.transform, mats.RingAdd, 0.5f, false, 8);
            var rfMain = ringFlat.main;
            rfMain.startLifetime = 0.42f;
            rfMain.startSpeed = 0f;
            rfMain.startSize = 3.8f;
            rfMain.startColor = StoryVfxPalette.GoldBright;
            ringFlat.emission.SetBursts(new[] { new ParticleSystem.Burst(0.02f, 1) });
            SetSizeCurve(ringFlat, AnimationCurve.EaseInOut(0f, 0.15f, 1f, 1.3f));
            SetFadeAlpha(ringFlat, 0.02f, 0.45f);
            ringFlat.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;

            // 放射火花
            var sparks = CreatePS("Sparks", root.transform, mats.SparkAdd, 0.7f, false, 80);
            var spMain = sparks.main;
            spMain.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.58f);
            spMain.startSpeed = new ParticleSystem.MinMaxCurve(6.0f, 13.0f);
            spMain.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            spMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.GoldWhite, StoryVfxPalette.GoldBright);
            spMain.gravityModifier = 0.7f;
            sparks.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 38) });
            var spShape = sparks.shape;
            spShape.enabled = true;
            spShape.shapeType = ParticleSystemShapeType.Sphere;
            spShape.radius = 0.15f;
            var spLimit = sparks.limitVelocityOverLifetime;
            spLimit.enabled = true;
            spLimit.limit = 1.2f;
            spLimit.dampen = 0.12f;
            SetFadeAlpha(sparks, 0.02f, 0.45f);
            var spRenderer = sparks.GetComponent<ParticleSystemRenderer>();
            spRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            spRenderer.velocityScale = 0.06f;
            spRenderer.lengthScale = 1.4f;

            // 短い中心光
            var core = CreatePS("CoreGlow", root.transform, mats.GlowAdd, 0.25f, false, 4);
            var cMain = core.main;
            cMain.startLifetime = 0.22f;
            cMain.startSpeed = 0f;
            cMain.startSize = 2.6f;
            cMain.startColor = StoryVfxPalette.GoldWhite;
            core.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            SetSizeCurve(core, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(0.25f, 1.1f), new Keyframe(1f, 0.1f)));
            SetFadeAlpha(core, 0.02f, 0.35f);
        }

        // P2: fx_sword_trail（王の剣の投擲に追従する金白軌跡）
        static void BuildSwordTrail(GameObject root, MaterialSet mats)
        {
            var trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.36f;
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = 0.26f;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.55f, 0.55f), new Keyframe(1f, 0f));
            var tg = new Gradient();
            tg.SetKeys(
                new[] { new GradientColorKey(StoryVfxPalette.GoldWhite, 0f), new GradientColorKey(StoryVfxPalette.GoldBright, 1f) },
                new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.65f, 0.5f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = tg;
            trail.sharedMaterial = mats.SparkAdd;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.autodestruct = false;

            var sparks = CreatePS("TrailSparks", root.transform, mats.StarAdd, 1.2f, true, 120);
            var sMain = sparks.main;
            sMain.simulationSpace = ParticleSystemSimulationSpace.World;
            sMain.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.55f);
            sMain.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            sMain.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.24f);
            sMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.GoldWhite, StoryVfxPalette.GoldBright);
            var sEm = sparks.emission;
            sEm.rateOverTime = 38f;
            var sShape = sparks.shape;
            sShape.enabled = true;
            sShape.shapeType = ParticleSystemShapeType.Sphere;
            sShape.radius = 0.14f;
            SetSizeCurve(sparks, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            SetFadeAlpha(sparks, 0.05f, 0.5f);
        }

        // P2: fx_darkness_aura（闇落ち：黒紫のもや＋暗い粒子。抽象表現のみ・赤飛沫なし）
        static void BuildDarknessAura(GameObject root, MaterialSet mats)
        {
            var mist = CreatePS("DarkMist", root.transform, mats.DustAlpha, 2.6f, true, 120);
            var mMain = mist.main;
            mMain.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.3f);
            mMain.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.95f);
            mMain.startSize = new ParticleSystem.MinMaxCurve(0.75f, 1.55f);
            mMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            mMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.DarkAuraA, StoryVfxPalette.DarkAuraB);
            mMain.gravityModifier = -0.14f;
            var mEm = mist.emission;
            mEm.rateOverTime = 24f;

            var mShape = mist.shape;
            mShape.enabled = true;
            mShape.shapeType = ParticleSystemShapeType.Circle;
            mShape.radius = 0.95f;
            mShape.rotation = new Vector3(-90f, 0f, 0f);

            var mNoise = mist.noise;
            mNoise.enabled = true;
            mNoise.strength = 0.35f;
            mNoise.frequency = 0.65f;
            mNoise.scrollSpeed = 0.3f;

            SetSizeCurve(mist, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.5f, 1.15f), new Keyframe(1f, 1.55f)));
            SetFadeAlpha(mist, 0.22f, 0.65f);

            var particles = CreatePS("DarkParticles", root.transform, mats.SoftAlpha, 2.4f, true, 120);
            var pMain = particles.main;
            pMain.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.1f);
            pMain.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.35f);
            pMain.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            pMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.DarkAuraA, StoryVfxPalette.DarkMote);
            pMain.gravityModifier = -0.18f;
            var pEm = particles.emission;
            pEm.rateOverTime = 28f;

            var pShape = particles.shape;
            pShape.enabled = true;
            pShape.shapeType = ParticleSystemShapeType.Cone;
            pShape.angle = 0f;
            pShape.radius = 0.9f;
            pShape.length = 0.4f;
            pShape.rotation = new Vector3(-90f, 0f, 0f);

            var pNoise = particles.noise;
            pNoise.enabled = true;
            pNoise.strength = 0.45f;
            pNoise.frequency = 0.8f;

            SetSizeCurve(particles, new AnimationCurve(
                new Keyframe(0f, 0.3f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
            SetFadeAlpha(particles, 0.15f, 0.6f);
        }

        // P2: fx_shard_swirl（七色の積み木欠片が対象の周囲を螺旋状に旋回）
        static void BuildShardSwirl(GameObject root, MaterialSet mats)
        {
            var swirl = CreatePS("SwirlShards", root.transform, mats.CubeLit, 2.6f, true, 140);
            var sMain = swirl.main;
            sMain.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.6f);
            sMain.startSpeed = 0f;
            sMain.startSize3D = true;
            sMain.startSizeX = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
            sMain.startSizeY = new ParticleSystem.MinMaxCurve(0.11f, 0.20f);
            sMain.startSizeZ = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
            sMain.startRotation3D = true;
            sMain.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sMain.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.BuildFixedGradient(StoryVfxPalette.SevenColors))
            {
                mode = ParticleSystemGradientMode.RandomColor
            };
            var swEm = swirl.emission;
            swEm.rateOverTime = 26f;

            var sShape = swirl.shape;
            sShape.enabled = true;
            sShape.shapeType = ParticleSystemShapeType.Circle;
            sShape.radius = 1.35f;
            sShape.radiusThickness = 0.25f;
            sShape.rotation = new Vector3(90f, 0f, 0f);

            // velocityOverLifetime の全カーブモードを Constant で統一（Unity のモード不一致エラー防止）
            var vel = swirl.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(0.68f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(3.4f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
            vel.radial = new ParticleSystem.MinMaxCurve(-0.18f);
            vel.speedModifier = new ParticleSystem.MinMaxCurve(1f);

            Enable3DRotation(swirl, -340f, 340f);
            SetSizeCurve(swirl, new AnimationCurve(
                new Keyframe(0f, 0.1f), new Keyframe(0.15f, 1f), new Keyframe(0.82f, 1f), new Keyframe(1f, 0f)));
            ConfigureCubeMeshRenderer(swirl, mats.CubeLit);
        }

        // P2: fx_arena_collapse（闘技場の積み木が広範囲で時間差崩落＋暗い煙）
        static void BuildArenaCollapse(GameObject root, MaterialSet mats)
        {
            var blocks = CreatePS("WaveBlocks", root.transform, mats.CubeLit, 2.8f, false, 180);
            blocks.transform.localPosition = new Vector3(0f, 3.2f, 0f);
            var bMain = blocks.main;
            bMain.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            bMain.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.4f);
            bMain.startSize3D = true;
            bMain.startSizeX = new ParticleSystem.MinMaxCurve(0.32f, 0.62f);
            bMain.startSizeY = new ParticleSystem.MinMaxCurve(0.24f, 0.45f);
            bMain.startSizeZ = new ParticleSystem.MinMaxCurve(0.32f, 0.62f);
            bMain.startRotation3D = true;
            bMain.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bMain.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bMain.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            bMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.BuildFixedGradient(StoryVfxPalette.TumikiColors))
            {
                mode = ParticleSystemGradientMode.RandomColor
            };
            bMain.gravityModifier = 1.75f;
            blocks.emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.00f, 24),
                new ParticleSystem.Burst(0.35f, 28),
                new ParticleSystem.Burst(0.75f, 32),
                new ParticleSystem.Burst(1.15f, 24),
            });

            var bShape = blocks.shape;
            bShape.enabled = true;
            bShape.shapeType = ParticleSystemShapeType.Box;
            bShape.scale = new Vector3(9.0f, 1.4f, 5.5f);

            Enable3DRotation(blocks, -360f, 360f);
            SetSizeCurve(blocks, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.78f, 1f), new Keyframe(1f, 0f)));
            ConfigureCubeMeshRenderer(blocks, mats.CubeLit);

            // 暗い煙
            var smoke = CreatePS("DarkSmoke", root.transform, mats.DustAlpha, 3.0f, false, 80);
            smoke.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            var sMain = smoke.main;
            sMain.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            sMain.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            sMain.startSize = new ParticleSystem.MinMaxCurve(1.5f, 2.8f);
            sMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sMain.startColor = StoryVfxPalette.DarkSmoke;
            sMain.gravityModifier = -0.12f;
            smoke.emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.08f, 14),
                new ParticleSystem.Burst(0.45f, 16),
                new ParticleSystem.Burst(0.90f, 18),
            });

            var sShape = smoke.shape;
            sShape.enabled = true;
            sShape.shapeType = ParticleSystemShapeType.Box;
            sShape.scale = new Vector3(8.5f, 0.6f, 4.8f);

            SetSizeCurve(smoke, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.9f));
            SetFadeAlpha(smoke, 0.15f, 0.55f);
        }

        // P2: fx_sparkle_burst（金色のきらめき・UI前面表示用）
        static void BuildSparkleBurst(GameObject root, MaterialSet mats)
        {
            var stars = CreatePS("GoldStars", root.transform, mats.StarAdd, 1.8f, false, 80);
            var sMain = stars.main;
            sMain.startLifetime = new ParticleSystem.MinMaxCurve(0.85f, 1.55f);
            sMain.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.65f);
            sMain.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.54f);
            sMain.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            sMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.GoldWhite, StoryVfxPalette.GoldBright);
            sMain.gravityModifier = -0.06f;
            stars.emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0.00f, 26),
                new ParticleSystem.Burst(0.18f, 14),
            });

            var sShape = stars.shape;
            sShape.enabled = true;
            sShape.shapeType = ParticleSystemShapeType.Box;
            sShape.scale = new Vector3(3.6f, 0.85f, 0.2f);

            // 瞬きカーブ
            SetSizeCurve(stars, new AnimationCurve(
                new Keyframe(0.00f, 0.0f),
                new Keyframe(0.15f, 1.0f),
                new Keyframe(0.38f, 0.45f),
                new Keyframe(0.62f, 0.95f),
                new Keyframe(0.82f, 0.35f),
                new Keyframe(1.00f, 0.0f)));
            SetFadeAlpha(stars, 0.05f, 0.65f);

            var dots = CreatePS("GlowDots", root.transform, mats.GlowAdd, 1.9f, false, 64);
            var dMain = dots.main;
            dMain.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            dMain.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 1.25f);
            dMain.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.24f);
            dMain.startColor = new ParticleSystem.MinMaxGradient(StoryVfxPalette.GoldBright, StoryVfxPalette.GoldWhite);
            dMain.gravityModifier = -0.10f;
            dots.emission.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 28) });
            var dShape = dots.shape;
            dShape.enabled = true;
            dShape.shapeType = ParticleSystemShapeType.Box;
            dShape.scale = new Vector3(3.8f, 0.95f, 0.2f);
            SetSizeCurve(dots, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 0f));
            SetFadeAlpha(dots, 0.08f, 0.55f);
        }

        //========================================
        // P3 プレハブ構築
        //========================================

        // P3: fx_dust_motes（光の中を舞うごく弱い埃）
        static void BuildDustMotes(GameObject root, MaterialSet mats)
        {
            var ps = CreatePS("Motes", root.transform, mats.SoftAlpha, 6.0f, true, 80);
            ps.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4.0f, 6.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.075f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.94f, 0.78f, 0.26f),
                new Color(1f, 0.88f, 0.68f, 0.14f));
            main.gravityModifier = -0.008f;
            var em = ps.emission;
            em.rateOverTime = 12f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(5.5f, 3.2f, 3.5f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.18f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.12f;

            SetFadeAlpha(ps, 0.25f, 0.75f);
        }

        //========================================
        // プレビューシーン生成（Build Settings は変更しない）
        //========================================

        static void BuildPreviewScene(StoryVfxCatalog catalog, bool overwrite, bool openAfter)
        {
            if (!overwrite && File.Exists(PreviewScenePath)) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // カメラ
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.14f, 0.15f, 0.19f, 1f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.fieldOfView = 42f;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camGo.AddComponent<AudioListener>();

            // ライト
            var keyGo = new GameObject("Key Light");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.95f, 0.88f);
            key.intensity = 1.4f;
            key.shadows = LightShadows.Soft;
            keyGo.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            var rimGo = new GameObject("Rim Light");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = new Color(0.75f, 0.86f, 1f);
            rim.intensity = 0.7f;
            rim.shadows = LightShadows.None;
            rimGo.transform.rotation = Quaternion.Euler(24f, 155f, 0f);

            // 床・サイズ基準オブジェクト（だるま相当の背丈 1.8m のダミー柱）
            Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
            var floorMat = new Material(urpLit);
            floorMat.SetColor("_BaseColor", new Color(0.24f, 0.25f, 0.29f, 1f));
            floorMat.SetFloat("_Smoothness", 0.2f);
            string floorMatPath = MatDir + "/mat_vfx_preview_floor.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(floorMatPath) == null)
                AssetDatabase.CreateAsset(floorMat, floorMatPath);
            else
                floorMat = AssetDatabase.LoadAssetAtPath<Material>(floorMatPath);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "PreviewFloor";
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            var dummyMat = new Material(urpLit);
            dummyMat.SetColor("_BaseColor", new Color(0.78f, 0.30f, 0.26f, 1f));
            string dummyMatPath = MatDir + "/mat_vfx_preview_dummy.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(dummyMatPath) == null)
                AssetDatabase.CreateAsset(dummyMat, dummyMatPath);
            else
                dummyMat = AssetDatabase.LoadAssetAtPath<Material>(dummyMatPath);

            var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dummy.name = "ReferenceDarumaSilhouette";
            dummy.transform.position = new Vector3(0f, 0.9f, 0f);
            dummy.transform.localScale = new Vector3(0.95f, 0.9f, 0.95f);
            dummy.GetComponent<Renderer>().sharedMaterial = dummyMat;

            // スポーンアンカー・軌跡アンカー
            var spawnAnchor = new GameObject("VfxSpawnAnchor");
            spawnAnchor.transform.position = new Vector3(0f, 0.85f, 0f);

            var orbitAnchor = new GameObject("VfxOrbitAnchor");
            orbitAnchor.transform.position = new Vector3(-2.5f, 1.3f, 0f);

            // EventSystem（Input System 対応）
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            esGo.AddComponent<InputSystemUIInputModule>();

            // StoryEffectPlayer + ScreenOverlayFx
            var playerGo = new GameObject("StoryEffectPlayer");
            var player = playerGo.AddComponent<StoryEffectPlayer>();
            var overlayGo = new GameObject("StoryScreenOverlayFx");
            overlayGo.transform.SetParent(playerGo.transform, false);
            var overlay = overlayGo.AddComponent<ScreenOverlayFx>();

            var vigSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TexDir + "/vfx_vignette.png");
            if (vigSprite != null)
            {
                var ovSo = new SerializedObject(overlay);
                ovSo.FindProperty("m_vignetteSprite").objectReferenceValue = vigSprite;
                ovSo.ApplyModifiedPropertiesWithoutUndo();
            }

            var pSo = new SerializedObject(player);
            pSo.FindProperty("m_catalog").objectReferenceValue = catalog;
            pSo.FindProperty("m_overlayFx").objectReferenceValue = overlay;
            pSo.FindProperty("m_defaultAnchor").objectReferenceValue = spawnAnchor.transform;
            pSo.ApplyModifiedPropertiesWithoutUndo();

            // プレビューコントローラ
            var ctrlGo = new GameObject("StoryVfxPreviewController");
            var ctrl = ctrlGo.AddComponent<StoryVfxPreviewController>();
            var cSo = new SerializedObject(ctrl);
            cSo.FindProperty("m_player").objectReferenceValue = player;
            cSo.FindProperty("m_catalog").objectReferenceValue = catalog;
            cSo.FindProperty("m_spawnAnchor").objectReferenceValue = spawnAnchor.transform;
            cSo.FindProperty("m_orbitAnchor").objectReferenceValue = orbitAnchor.transform;
            cSo.FindProperty("m_camera").objectReferenceValue = cam;
            cSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, PreviewScenePath);
            Debug.Log($"[StoryVfxGenerator] プレビューシーンを生成しました: {PreviewScenePath}");

            if (openAfter)
            {
                EditorSceneManager.OpenScene(PreviewScenePath, OpenSceneMode.Single);
            }
        }
    }
}
#endif
