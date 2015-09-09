using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Universal.Torrent.Bencoding;

namespace Universal.Torrent.Common
{
    public class RawTrackerTier : IList<string>
    {
        public RawTrackerTier()
            : this(new BEncodedList())
        {
        }

        public RawTrackerTier(BEncodedList tier)
        {
            Tier = tier;
        }

        public RawTrackerTier(IEnumerable<string> announces)
            : this()
        {
            foreach (var v in announces)
                Add(v);
        }

        public static IList<RawTrackerTier> CreateTiers(IEnumerable<string> trackers)
        {
            return trackers.Select(tracker => new RawTrackerTier(new[] {tracker})).ToList();
        }

        internal BEncodedList Tier { get; set; }

        public string this[int index]
        {
            get { return ((BEncodedString) Tier[index]).Text; }
            set { Tier[index] = new BEncodedString(value); }
        }

        public int IndexOf(string item)
        {
            return Tier.IndexOf((BEncodedString) item);
        }

        public void Insert(int index, string item)
        {
            Tier.Insert(index, (BEncodedString) item);
        }

        public void RemoveAt(int index)
        {
            Tier.RemoveAt(index);
        }

        public void Add(string item)
        {
            Tier.Add((BEncodedString) item);
        }

        public void Clear()
        {
            Tier.Clear();
        }

        public bool Contains(string item)
        {
            return Tier.Contains((BEncodedString) item);
        }

        public void CopyTo(string[] array, int arrayIndex)
        {
            foreach (var s in this)
                array[arrayIndex ++] = s;
        }

        public bool Remove(string item)
        {
            return Tier.Remove((BEncodedString) item);
        }

        public int Count => Tier.Count;

        public bool IsReadOnly => Tier.IsReadOnly;

        public IEnumerator<string> GetEnumerator()
        {
            return (from BEncodedString v in Tier select v.Text).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}