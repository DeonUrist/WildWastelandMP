using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class CrawlAssetBuild
{
    public static void Build()
    {
        const string path = "Assets/LowCrawl.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.SaveAndReimport();
        var takes = importer.defaultClipAnimations;
        if (takes.Length != 1) throw new InvalidOperationException("Expected one Low Crawl take; found " + takes.Length);
        var take = takes[0];
        importer.clipAnimations = new[] { new ModelImporterClipAnimation {
            name = "LowCrawl", takeName = take.takeName, firstFrame = take.firstFrame, lastFrame = take.lastFrame,
            loopTime = true, loopPose = true,
            lockRootRotation = true, keepOriginalOrientation = true,
            lockRootHeightY = true, keepOriginalPositionY = true, heightFromFeet = false,
            lockRootPositionXZ = false, keepOriginalPositionXZ = true
        } };
        importer.SaveAndReimport();
        Avatar avatar = null; AnimationClip clip = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Avatar) avatar = (Avatar)asset;
            var candidate = asset as AnimationClip;
            if (candidate != null && candidate.name == "LowCrawl") clip = candidate;
        }
        if (avatar == null || !avatar.isValid || !avatar.isHuman || clip == null || !clip.humanMotion)
            throw new InvalidOperationException("Low Crawl needs a valid humanoid avatar and clip.");
        var copy = new AnimationClip(); EditorUtility.CopySerialized(clip, copy);
        AssetDatabase.DeleteAsset("Assets/LowCrawl.anim"); AssetDatabase.CreateAsset(copy, "Assets/LowCrawl.anim");
        AssetDatabase.SaveAssets(); Directory.CreateDirectory("Build");
        var build = new AssetBundleBuild { assetBundleName = "low-crawl.bundle", assetNames = new[] { "Assets/LowCrawl.anim" } };
        var result = BuildPipeline.BuildAssetBundles("Build", new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        if (result == null) throw new InvalidOperationException("Low Crawl bundle build failed.");
        Debug.Log("Low Crawl authored: humanoid=" + clip.humanMotion + "; duration=" + clip.length + "; rate=" + clip.frameRate + "; average speed=" + clip.averageSpeed + "; frames=" + take.firstFrame + ".." + take.lastFrame);
    }
}
