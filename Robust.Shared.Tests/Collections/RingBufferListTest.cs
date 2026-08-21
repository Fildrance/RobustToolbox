using NUnit.Framework;
using Robust.Shared.Collections;

namespace Robust.Shared.Tests.Collections;

[Parallelizable(ParallelScope.All | ParallelScope.Fixtures)]
[TestFixture, TestOf(typeof(RingBufferList<>))]
internal sealed class RingBufferListTest
{
    [Test]
    public void TestBasicAdd()
    {
        var list = new RingBufferList<int>();
        list.Add(1);
        list.Add(2);
        list.Add(3);

        Assert.That(list, Is.EquivalentTo(new[] {1, 2, 3}));
    }

    [Test]
    public void TestBasicAddAfterWrap()
    {
        var list = new RingBufferList<int>(6);
        list.Add(1);
        list.Add(2);
        list.Add(3);
        list.RemoveAt(0);
        list.Add(4);
        list.Add(5);
        list.Add(6);

        Assert.Multiple(() =>
        {
            // Ensure wrapping properly happened and we didn't expand.
            // (one slot is wasted by nature of implementation)
            Assert.That(list.Capacity, Is.EqualTo(6));
            Assert.That(list, Is.EquivalentTo(new[] { 2, 3, 4, 5, 6 }));
        });
    }

    [Test]
    public void TestMiddleRemoveAtScenario1()
    {
        var list = new RingBufferList<int>(6);
        list.Add(-1);
        list.Add(-1);
        list.Add(-1);
        list.Add(-1);
        list.Add(1);
        list.RemoveAt(0);
        list.RemoveAt(0);
        list.RemoveAt(0);
        list.RemoveAt(0);
        list.Add(2);
        list.Add(3);
        list.Add(4);
        list.Add(5);
        list.Remove(4);

        Assert.That(list, Is.EquivalentTo(new[] {1, 2, 3, 5}));
    }

    [Test]
    public void TestMiddleRemoveAtScenario2()
    {
        var list = new RingBufferList<int>(6);
        list.Add(-1);
        list.Add(-1);
        list.Add(1);
        list.RemoveAt(0);
        list.RemoveAt(0);
        list.Add(2);
        list.Add(3);
        list.Add(4);
        list.Add(5);
        list.Remove(3);

        Assert.That(list, Is.EquivalentTo(new[] {1, 2, 4, 5}));
    }

    [Test]
    public void TestMiddleRemoveAtScenario3()
    {
        var list = new RingBufferList<int>(6);
        list.Add(1);
        list.Add(2);
        list.Add(3);
        list.Add(4);
        list.Add(5);
        list.Remove(4);

        Assert.That(list, Is.EquivalentTo(new[] {1, 2, 3, 5}));
    }

    [Test]
    public void TestIndexOfWithComparer()
    {
        var id = Guid.NewGuid();
        var list = new RingBufferList<EntryWithIdForTest>
        {
            new() { Id = Guid.NewGuid(), Payload = 1 },
            new() { Id = id, Payload = 2 },
            new() { Id = Guid.NewGuid(), Payload = 3 }
        };

        Assert.Multiple(() =>
        {
            Assert.That(list.IndexOf(new EntryWithIdForTest { Id = id, Payload = 999 }, EntryWithIdComparerForTest.Instance), Is.EqualTo(1));
            Assert.That(list.IndexOf(new EntryWithIdForTest { Id = Guid.NewGuid() }, EntryWithIdComparerForTest.Instance), Is.EqualTo(-1));
        });
    }

    private struct EntryWithIdForTest
    {
        public Guid Id;
        public int Payload;
    }

    private sealed class EntryWithIdComparerForTest : IEqualityComparer<EntryWithIdForTest>
    {
        public static readonly EntryWithIdComparerForTest Instance = new();

        public bool Equals(EntryWithIdForTest x, EntryWithIdForTest y)
        {
            return x.Id == y.Id;
        }

        public int GetHashCode(EntryWithIdForTest obj)
        {
            return obj.Id.GetHashCode();
        }
    }
}
