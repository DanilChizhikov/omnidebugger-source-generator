using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace DTech.OmniDebugger.SourceGenerator
{
	internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
		where T : IEquatable<T>
	{
		private readonly ImmutableArray<T> _items;

		public int Length => _items.IsDefault ? 0 : _items.Length;

		public T this[int index] => _items[index];

		public EquatableArray(ImmutableArray<T> items)
		{
			_items = items;
		}

		public bool Equals(EquatableArray<T> other)
		{
			if (Length != other.Length)
			{
				return false;
			}

			for (int i = 0; i < Length; i++)
			{
				if (!_items[i].Equals(other._items[i]))
				{
					return false;
				}
			}

			return true;
		}

		public override bool Equals(object obj) => obj is EquatableArray<T> other && Equals(other);

		public override int GetHashCode()
		{
			int hash = 17;
			for (int i = 0; i < Length; i++)
			{
				hash = hash * 31 + (_items[i]?.GetHashCode() ?? 0);
			}

			return hash;
		}

		public IEnumerator<T> GetEnumerator() =>
			(_items.IsDefault ? Enumerable.Empty<T>() : _items).GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
