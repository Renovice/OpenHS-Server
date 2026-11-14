using System.Collections;
using System.Collections.Generic;
using System.Text;
using SabberStoneCore.Enchants;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Auras
{
	/// <summary>
	/// A container class for multiple auras. Use this when you want to implement a <see cref="Power"/> with multiple auras.
	/// </summary>
	public class MultiAura : IAura, IReadOnlyList<IAura>
	{
		private readonly IReadOnlyList<IAura> _auras;

		public IPlayable Owner { get; set; }
		public bool On { get; set; } = true;

		public MultiAura(params IAura[] auras)
		{
			_auras = auras;
		}

		public void Update()
		{
			if (!On)
				Owner.Game.Auras.Remove(this);
			else
				for (int i = 0; i < _auras.Count; i++)
					_auras[i].Update();
		}

		public void Remove()
		{
			On = false;
			Owner.OngoingEffect = null;
			for (int i = 0; i < _auras.Count; i++)
				_auras[i].Remove();
		}

		void IAura.Activate(IPlayable owner)
		{
			ActivateInternal(owner);
		}

		public void Clone(IPlayable clone)
		{
			ActivateInternal(clone, true);
		}

		public IEnumerator<IAura> GetEnumerator()
		{
			return _auras.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public int Count => _auras.Count;

		public IAura this[int index] => _auras[index];

		public override string ToString()
		{
			var sb = new StringBuilder("[MA:");
			foreach (IAura a in _auras)
				sb.Append($"{{{a}}}");
			sb.Append("]");
			return sb.ToString();
		}

		/// <summary>HsServer: the hand cost effect of a card's aura, alone or inside a <see cref="MultiAura"/> (a card with a
		/// cost aura and a board aura: a Zilliax Deluxe 3000 built from Ticking and Pylon Module). The hand activates it
		/// (HandZone); a MultiAura on the board skips it.</summary>
		public static AdaptiveCostEffect CostEffect(IAura aura)
		{
			if (aura is AdaptiveCostEffect cost) return cost;
			if (aura is MultiAura multi)
				foreach (IAura a in multi._auras)
					if (a is AdaptiveCostEffect inner) return inner;
			return null;
		}

		private void ActivateInternal(IPlayable owner, bool cloning = false)
		{
			var auras = new List<IAura>(_auras.Count);

			for (int i = 0; i < _auras.Count; i++)
			{
				// HsServer: a hand cost effect isn't a board aura: it activates nothing here (AdaptiveCostEffect returns at
				// once outside the hand), so taking the last game aura would have taken another card's
				if (_auras[i] is AdaptiveCostEffect) continue;
				if (cloning)
					_auras[i].Clone(owner);
				else
					_auras[i].Activate(owner);
				auras.Add(owner.OngoingEffect);
				owner.Game.Auras.RemoveAt(owner.Game.Auras.Count - 1);
			}

			IAura instance = new MultiAura(auras.ToArray())
			{
				Owner = owner,
			};
			owner.Game.Auras.Add(instance);
			owner.OngoingEffect = instance;
		}


	}
}
