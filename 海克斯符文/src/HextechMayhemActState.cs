namespace HextechRunes;

internal sealed class HextechMayhemActState
{
	private int[] _rarityByAct = NewUnknownArray();
	private int[] _monsterHexByAct = NewUnknownArray();
	private int[] _resolvedActs = NewResolvedArray();

	public int ActCount => _resolvedActs.Length;

	public int[] SavedRarityByAct
	{
		get => _rarityByAct;
		set => _rarityByAct = NormalizeUnknownArray(value);
	}

	public int[] SavedMonsterHexByAct
	{
		get => _monsterHexByAct;
		set => _monsterHexByAct = NormalizeUnknownArray(value);
	}

	public int[] SavedResolvedActs
	{
		get => _resolvedActs;
		set => _resolvedActs = NormalizeResolvedArray(value);
	}

	public bool IsResolved(int actIndex)
	{
		return actIndex >= 0 && actIndex < _resolvedActs.Length && _resolvedActs[actIndex] > 0;
	}

	public void SetResolved(int actIndex, bool resolved)
	{
		if (actIndex >= 0 && actIndex < _resolvedActs.Length)
		{
			_resolvedActs[actIndex] = resolved ? 1 : 0;
		}
	}

	public bool TryMarkResolved(int actIndex)
	{
		if (actIndex < 0 || actIndex >= _resolvedActs.Length || _resolvedActs[actIndex] > 0)
		{
			return false;
		}

		_resolvedActs[actIndex] = 1;
		return true;
	}

	public HextechRarityTier? GetRarity(int actIndex)
	{
		if (actIndex < 0 || actIndex >= _rarityByAct.Length || _rarityByAct[actIndex] < 0)
		{
			return null;
		}

		return (HextechRarityTier)_rarityByAct[actIndex];
	}

	public void SetRarity(int actIndex, HextechRarityTier rarity)
	{
		if (actIndex >= 0 && actIndex < _rarityByAct.Length)
		{
			_rarityByAct[actIndex] = (int)rarity;
		}
	}

	public bool TrySetRarityIfMissing(int actIndex, HextechRarityTier rarity)
	{
		if (actIndex < 0 || actIndex >= _rarityByAct.Length || _rarityByAct[actIndex] >= 0)
		{
			return false;
		}

		_rarityByAct[actIndex] = (int)rarity;
		return true;
	}

	public MonsterHexKind? GetMonsterHex(int actIndex)
	{
		if (actIndex < 0 || actIndex >= _monsterHexByAct.Length || _monsterHexByAct[actIndex] < 0)
		{
			return null;
		}

		return (MonsterHexKind)_monsterHexByAct[actIndex];
	}

	public void SetMonsterHex(int actIndex, MonsterHexKind hex)
	{
		if (actIndex >= 0 && actIndex < _monsterHexByAct.Length)
		{
			_monsterHexByAct[actIndex] = (int)hex;
		}
	}

	public void ClearMonsterHex(int actIndex)
	{
		if (actIndex >= 0 && actIndex < _monsterHexByAct.Length)
		{
			_monsterHexByAct[actIndex] = -1;
		}
	}

	public IReadOnlyList<MonsterHexKind> GetActiveMonsterHexes(int currentActIndex, Func<int, bool> shouldRecoverMonsterHex)
	{
		List<MonsterHexKind> result = new();
		HashSet<MonsterHexKind> seen = new();
		for (int actIndex = 0; actIndex <= currentActIndex && actIndex < _monsterHexByAct.Length; actIndex++)
		{
			if (_monsterHexByAct[actIndex] >= 0
				&& (IsResolved(actIndex) || shouldRecoverMonsterHex(actIndex)))
			{
				MonsterHexKind hex = (MonsterHexKind)_monsterHexByAct[actIndex];
				if (seen.Add(hex))
				{
					result.Add(hex);
				}
			}
		}

		return result;
	}

	public int LastActIndexFor(int maxActIndex)
	{
		return Math.Min(maxActIndex, _resolvedActs.Length - 1);
	}

	public void Reset()
	{
		_rarityByAct = NewUnknownArray();
		_monsterHexByAct = NewUnknownArray();
		_resolvedActs = NewResolvedArray();
	}

	public void DebugSetOnlyMonsterHex(int actIndex, MonsterHexKind hex, HextechRarityTier rarity)
	{
		Reset();
		if (actIndex >= 0 && actIndex < _monsterHexByAct.Length)
		{
			_rarityByAct[actIndex] = (int)rarity;
			_monsterHexByAct[actIndex] = (int)hex;
			_resolvedActs[actIndex] = 1;
		}
	}

	public string Describe()
	{
		return $"resolved={string.Join(",", _resolvedActs)} rarity={string.Join(",", _rarityByAct)} monster={string.Join(",", _monsterHexByAct)}";
	}

	private static int[] NewUnknownArray()
	{
		return [ -1, -1, -1 ];
	}

	private static int[] NewResolvedArray()
	{
		return [ 0, 0, 0 ];
	}

	private static int[] NormalizeUnknownArray(int[]? value)
	{
		int[] normalized = NewUnknownArray();
		if (value == null)
		{
			return normalized;
		}

		for (int i = 0; i < Math.Min(normalized.Length, value.Length); i++)
		{
			normalized[i] = value[i];
		}

		return normalized;
	}

	private static int[] NormalizeResolvedArray(int[]? value)
	{
		int[] normalized = NewResolvedArray();
		if (value == null)
		{
			return normalized;
		}

		for (int i = 0; i < Math.Min(normalized.Length, value.Length); i++)
		{
			normalized[i] = value[i] > 0 ? 1 : 0;
		}

		return normalized;
	}
}
