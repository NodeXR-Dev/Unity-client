using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// glTFast 셰이더를 Graphics Settings 의 Always Included Shaders 에 등록하는 에디터 도구.
//   메뉴: Tools > NodeXR > glTFast 셰이더 빌드에 포함
//
// [왜 필요한가]
// 런타임에 로드하는 GLB 의 머티리얼은 glTFast 의 Shader Graph 로 렌더된다.
// 이 셰이더들은 씬에서 참조되지 않으므로 빌드 시 스트리핑되고, 빌드에서 Shader.Find 가
// 실패해 모델이 전부 핑크(셰이더 없음)로 보인다. 에디터에서는 on-demand 컴파일이라
// 정상으로 보여서 빌드에서만 드러난다.
//
// [왜 수동으로 못 넣는가]
// 셰이더가 Packages/ 아래에 있는데 Graphics Settings 의 셰이더 피커는 Assets/ 만 보여준다
// (Unity 버전에 따라 Packages 탭이 없다). 그래서 코드로 AssetDatabase 를 통해 넣는다.
//
// 참고: 패키지 문서 Documentation~/ProjectSetup.md "Materials and Shader Variants"
public static class GltfastShaderIncluder
{
    private const string PackagePath = "Packages/com.atteneder.gltfast/Runtime/Shader/";

    // 런타임(ShaderGraphMaterialGenerator)이 Shader.Find 로 찾는 셰이더들.
    // 파일명과 셰이더 이름이 같다.
    private static readonly string[] ShaderFiles =
    {
        "glTF-pbrMetallicRoughness.shadergraph",
        "glTF-unlit.shadergraph",
        "glTF-pbrSpecularGlossiness.shadergraph",
        "URP/glTF-pbrMetallicRoughness-Clearcoat.shadergraph",
    };

    [MenuItem("Tools/NodeXR/glTFast 셰이더 빌드에 포함")]
    public static void IncludeShaders()
    {
        var graphicsSettings =
            AssetDatabase.LoadAssetAtPath<Object>(
                "ProjectSettings/GraphicsSettings.asset");
        if (graphicsSettings == null)
        {
            Debug.LogError("[GltfastShaderIncluder] GraphicsSettings.asset 을 열지 못했습니다.");
            return;
        }

        var so = new SerializedObject(graphicsSettings);
        SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
        if (list == null)
        {
            Debug.LogError("[GltfastShaderIncluder] m_AlwaysIncludedShaders 를 찾지 못했습니다.");
            return;
        }

        // 이미 들어있는 셰이더 수집(중복 추가 방지)
        var existing = new HashSet<Object>();
        for (int i = 0; i < list.arraySize; i++)
        {
            Object o = list.GetArrayElementAtIndex(i).objectReferenceValue;
            if (o != null)
                existing.Add(o);
        }

        int added = 0;
        var missing = new List<string>();

        foreach (string file in ShaderFiles)
        {
            string path = PackagePath + file;
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                missing.Add(path);
                continue;
            }
            if (existing.Contains(shader))
            {
                Debug.Log($"[GltfastShaderIncluder] 이미 포함됨: {shader.name}");
                continue;
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            existing.Add(shader);
            added++;
            Debug.Log($"[GltfastShaderIncluder] 추가: {shader.name}  ({path})");
        }

        if (added > 0)
        {
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[GltfastShaderIncluder] 완료 — {added}개 추가했습니다. 재빌드가 필요합니다.");
        }
        else
        {
            Debug.Log("[GltfastShaderIncluder] 새로 추가할 셰이더가 없습니다.");
        }

        foreach (string path in missing)
        {
            // Clearcoat 등 일부는 렌더 파이프라인/버전에 따라 없을 수 있다. 치명적이지 않다.
            Debug.LogWarning($"[GltfastShaderIncluder] 셰이더를 찾지 못했습니다(건너뜀): {path}");
        }
    }

    // 지금 무엇이 포함돼 있는지 확인용.
    [MenuItem("Tools/NodeXR/Always Included Shaders 목록 출력")]
    public static void ListIncludedShaders()
    {
        var graphicsSettings =
            AssetDatabase.LoadAssetAtPath<Object>(
                "ProjectSettings/GraphicsSettings.asset");
        if (graphicsSettings == null) return;

        var so = new SerializedObject(graphicsSettings);
        SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
        for (int i = 0; i < list.arraySize; i++)
        {
            Object o = list.GetArrayElementAtIndex(i).objectReferenceValue;
            Debug.Log($"[{i}] {(o != null ? o.name : "(null)")}");
        }
    }
}
