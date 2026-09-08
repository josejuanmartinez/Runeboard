using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Temporary Editor-only GPU check; removed after reading the report.
[InitializeOnLoad]
public static class HexEdgeVerification
{
    static readonly double readyAt;
    static HexEdgeVerification() { readyAt=EditorApplication.timeSinceStartup+3; EditorApplication.update+=Run; }
    [Serializable] class Check { public bool shaderErrors; public int fogOffAlphaDifferences, visibleAlphaDifferences, oldFogAlphaDifferences; public float edgeCrop, fogFade; }
    static Texture2D Draw(Texture source, Material material)
    {
        var previous=RenderTexture.active;
        var rt=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32);
        RenderTexture.active=rt; GL.Clear(true,true,Color.clear);
        if (material == null) Graphics.Blit(source,rt); else Graphics.Blit(source,rt,material);
        RenderTexture.active=rt;
        var result=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);
        result.ReadPixels(new Rect(0,0,source.width,source.height),0,0); result.Apply();
        RenderTexture.active=previous; RenderTexture.ReleaseTemporary(rt); return result;
    }
    static int Different(Texture2D a,Texture2D b)
    {
        var x=a.GetPixels32(); var y=b.GetPixels32(); int n=0;
        for(int i=0;i<x.Length;i++) if(Math.Abs(x[i].a-y[i].a)>1) n++;
        return n;
    }
    static void Run()
    {
        if(EditorApplication.isCompiling || EditorApplication.timeSinceStartup<readyAt) return;
        EditorApplication.update-=Run;
        string output=Path.GetFullPath("output/hex-prefab-standardization/edge-fix"); Directory.CreateDirectory(output);
        var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/HexSeamlessBlendGame.mat");
        var sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Hexes/Tiles/wastelands_09.png");
        if(source==null || sprite==null) return;
        var material=new Material(source);
        float width=974f/750f*1.0575679f;
        float dx=1.09f/width,dy=.825f/width;
        var offsets=new[]{new Vector4(dx/2,-dy),new Vector4(dx,0),new Vector4(dx/2,dy),new Vector4(-dx/2,dy),new Vector4(-dx,0),new Vector4(-dx/2,-dy)};
        material.SetVector("_SpriteUV",new Vector4(0,0,1,1));
        material.SetFloat("_AspectY",1314f/750f*1.0672704f/width); material.SetFloat("_GridOn",0);
        for(int i=0;i<6;i++)
        {
            material.SetVector("_NeighborOffset"+i,offsets[i]); material.SetFloat("_NeighborValid"+i,-1);
            material.SetTexture("_NeighborTex"+i,sprite.texture); material.SetVector("_NeighborUV"+i,new Vector4(0,0,1,1));
        }
        var reference=Draw(sprite.texture,null);
        var fixedFog=Draw(sprite.texture,material);
        var check=new Check { shaderErrors=ShaderUtil.ShaderHasError(source.shader),edgeCrop=source.GetFloat("_EdgeCrop"),fogFade=source.GetFloat("_FogFade"),fogOffAlphaDifferences=Different(reference,fixedFog) };
        material.SetFloat("_FogFade",.16f);
        var oldFog=Draw(sprite.texture,material); check.oldFogAlphaDifferences=Different(reference,oldFog);
        material.SetFloat("_FogFade",0);
        for(int i=0;i<6;i++) material.SetFloat("_NeighborValid"+i,1);
        var visible=Draw(sprite.texture,material); check.visibleAlphaDifferences=Different(reference,visible);
        File.WriteAllText(Path.Combine(output,"gpu-verification.json"),JsonUtility.ToJson(check,true));
        File.WriteAllBytes(Path.Combine(output,"fog-after.png"),fixedFog.EncodeToPNG());
        File.WriteAllBytes(Path.Combine(output,"fog-before.png"),oldFog.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(material);
        foreach(var t in new[]{reference,fixedFog,oldFog,visible}) UnityEngine.Object.DestroyImmediate(t);
    }
}
