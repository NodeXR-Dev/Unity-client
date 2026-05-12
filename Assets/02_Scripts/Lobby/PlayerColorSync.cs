using UnityEngine;
using Fusion;
using System.Collections.Generic;

public class PlayerColorSync : NetworkBehaviour
{
    // 스킨과 옷의 색상 인덱스를 각각 네트워크로 동기화
    [Networked, OnChangedRender(nameof(UpdateVisuals))]
    public int SkinIndex { get; set; } = -1;

    [Networked, OnChangedRender(nameof(UpdateVisuals))]
    public int ClothesIndex { get; set; } = -1;

    private SkinnedMeshRenderer headRenderer;
    private static MaterialPropertyBlock _propBlock;

    private readonly Color[] palette = {
        Color.red, 
        Color.yellow, 
        Color.green, 
        Color.blue,
        new Color(1f, 0.41f, 0.71f) // 분홍색
    };

    public override void Spawned()
    {
        if (headRenderer == null) headRenderer = GetComponentInChildren<SkinnedMeshRenderer>();

        UpdateVisuals();

        if (Object.HasStateAuthority)
        {
            // 중복되지 않는 "스킨+옷" 조합 결정
            DetermineUniqueCombination();
        }
    }

    // [추가] Fusion의 표준 시각화 업데이트 메서드
    public override void Render()
    {
        // 데이터가 유효할 때만 시각화 업데이트 호출
        if (SkinIndex != -1 && ClothesIndex != -1)
        {
            UpdateVisuals();
        }
    }

    private void DetermineUniqueCombination()
    {
        // 현재 방에 있는 모든 플레이어의 조합(SkinIndex, ClothesIndex)을 파악
        HashSet<int> takenCombinations = new HashSet<int>();
        var allPlayers = Object.Runner.GetAllBehaviours<PlayerColorSync>();
        
        foreach (var p in allPlayers)
        {
            if (p != this && p.SkinIndex != -1 && p.ClothesIndex != -1)
            {
                // 조합을 고유 숫자로 변환 (예: 스킨0, 옷2 -> 02)
                int comboId = (p.SkinIndex * 10) + p.ClothesIndex;
                takenCombinations.Add(comboId);
            }
        }

        // 가능한 모든 조합 리스트 생성 (5x5 = 25가지)
        List<(int skin, int clothes)> availableCombos = new List<(int, int)>();
        for (int s = 0; s < palette.Length; s++)
        {
            for (int c = 0; c < palette.Length; c++)
            {
                int currentComboId = (s * 10) + c;
                if (!takenCombinations.Contains(currentComboId))
                {
                    availableCombos.Add((s, c));
                }
            }
        }

        // 남은 조합 중 하나를 랜덤으로 선택
        if (availableCombos.Count > 0)
        {
            var selected = availableCombos[Random.Range(0, availableCombos.Count)];
            SkinIndex = selected.skin;
            ClothesIndex = selected.clothes;
        }
        else
        {
            // 만약 25명이 넘어서 조합이 다 찼다면 완전 랜덤
            SkinIndex = Random.Range(0, palette.Length);
            ClothesIndex = Random.Range(0, palette.Length);
        }

        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (headRenderer == null) headRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
        if (headRenderer == null || SkinIndex < 0 || ClothesIndex < 0) return;

        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

        // 1. 스킨 색상 적용 (0번 슬롯)
        headRenderer.GetPropertyBlock(_propBlock, 0);
        _propBlock.SetColor("_BaseColor", palette[SkinIndex]);
        headRenderer.SetPropertyBlock(_propBlock, 0);

        // 2. 옷 색상 적용 (2번 슬롯)
        headRenderer.GetPropertyBlock(_propBlock, 2);
        _propBlock.SetColor("_BaseColor", palette[ClothesIndex]);
        headRenderer.SetPropertyBlock(_propBlock, 2);
    }
}