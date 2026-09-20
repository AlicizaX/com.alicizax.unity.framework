using AlicizaX;
using NUnit.Framework;
using UnityEngine;

namespace AlicizaX.GameObjectPool.Tests
{
    public sealed class PoolCatalogContractTests
    {
        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("   ", "")]
        [TestCase("Assets/Bundles/UI/Window.prefab", "UI/Window")]
        [TestCase(" Assets/Bundles/UI/Window.prefab ", "UI/Window")]
        [TestCase("Assets/Bundle/Foo/Bar.prefab", "Foo/Bar")]
        [TestCase("assets/bundles/UI/Window.PREFAB", "UI/Window")]
        [TestCase(@"UI\A\B.prefab", "UI/A/B")]
        [TestCase("UI/A/B/", "UI/A/B")]
        [TestCase("UI/A/B///", "UI/A/B")]
        [TestCase("UI/A.B/C.prefab", "UI/A.B/C")]
        [TestCase("UI/A.B", "UI/A")]
        [TestCase("/", "")]
        [TestCase("\\\\", "")]
        public void NormalizeLocationStripsPrefixExtensionSlashAndWhitespace(string input, string expected)
        {
            Assert.That(PoolEntry.NormalizeLocation(input), Is.EqualTo(expected));
        }

        [Test]
        public void NormalizeClampsCapacityAndClearsIdleSecondsForNonBurst()
        {
            var fixedEntry = GameObjectPoolFixture.Entry("fx/a", PoolPolicy.Fixed, minIdle: -3, soft: 0, hard: 2, idle: 9f);
            Assert.That(fixedEntry.minIdle, Is.EqualTo(0));
            Assert.That(fixedEntry.softCapacity, Is.EqualTo(1));
            Assert.That(fixedEntry.hardCapacity, Is.EqualTo(2));
            Assert.That(fixedEntry.idleSeconds, Is.Zero);
            var sticky = GameObjectPoolFixture.Entry("fx/b", PoolPolicy.Sticky, minIdle: 20, soft: 4, hard: 3, idle: 4f);
            Assert.That(sticky.softCapacity, Is.EqualTo(4));
            Assert.That(sticky.hardCapacity, Is.EqualTo(4));
            Assert.That(sticky.minIdle, Is.EqualTo(4));
            Assert.That(sticky.idleSeconds, Is.Zero);
            var burst = GameObjectPoolFixture.Entry("fx/c", PoolPolicy.Burst, idle: -1f);
            Assert.That(burst.idleSeconds, Is.Zero);
        }

        [Test]
        public void GlobStarMatchesOneSegmentAndQuestionAndStarMatchInsideSegment()
        {
            var star = PoolGlobMatcher.Compile("fx/*/hit");
            Assert.That(star.IsLiteralPattern, Is.False);
            Assert.That(star.IsMatch("fx/a/hit"), Is.True);
            Assert.That(star.IsMatch("fx/a/b/hit"), Is.False);
            Assert.That(star.IsMatch("fx/hit"), Is.False);
            var pattern = PoolGlobMatcher.Compile("fx/h?t*");
            Assert.That(pattern.IsMatch("fx/hit"), Is.True);
            Assert.That(pattern.IsMatch("fx/hatXX"), Is.True);
            Assert.That(pattern.IsMatch("fx/ht"), Is.False);
            Assert.That(pattern.IsMatch("fx/h/t"), Is.False);
        }

        [Test]
        public void GlobRecursiveMatchesZeroOrMoreSegmentsAndCollapsesConsecutiveMarkers()
        {
            var matcher = PoolGlobMatcher.Compile("fx/**/hit");
            Assert.That(matcher.IsMatch("fx/hit"), Is.True);
            Assert.That(matcher.IsMatch("fx/a/hit"), Is.True);
            Assert.That(matcher.IsMatch("fx/a/b/hit"), Is.True);
            Assert.That(matcher.IsMatch("fx/a/miss"), Is.False);
            var collapsed = PoolGlobMatcher.Compile("fx/**/**/hit");
            Assert.That(collapsed.IsMatch("fx/hit"), Is.True);
            Assert.That(collapsed.IsMatch("fx/a/b/hit"), Is.True);
            var emptySeg = PoolGlobMatcher.Compile("fx//a");
            Assert.That(emptySeg.IsMatch("fx/a"), Is.True);
            Assert.That(PoolGlobMatcher.Compile("").IsValid, Is.False);
            Assert.That(PoolGlobMatcher.Compile(null).IsMatch("x"), Is.False);
        }

        [Test]
        public void CatalogExactWinsThenGlobAndCachesTheGlobHit()
        {
            var catalog = PoolCompiledCatalog.Build(new[]
            {
                GameObjectPoolFixture.Entry("fx/hit", PoolPolicy.Fixed, name: "exact", priority: 0),
                GameObjectPoolFixture.Entry("fx/*", PoolPolicy.Burst, name: "glob", priority: 10)
            });
            try
            {
                int exact = catalog.Resolve("fx/hit");
                Assert.That(catalog.GetRule(exact).EntryName, Is.EqualTo("exact"));
                int glob = catalog.Resolve("fx/miss");
                Assert.That(catalog.GetRule(glob).EntryName, Is.EqualTo("glob"));
                Assert.That(catalog.Resolve("fx/miss"), Is.EqualTo(glob));
                Assert.That(catalog.Resolve("other"), Is.EqualTo(-1));
                Assert.That(catalog.Resolve(""), Is.EqualTo(-1));
                Assert.That(catalog.Resolve(null), Is.EqualTo(-1));
            }
            finally { catalog.Dispose(); }
        }

        [Test]
        public void DuplicateLiteralKeepsFirstAfterPrioritySort()
        {
            var catalog = PoolCompiledCatalog.Build(new[]
            {
                GameObjectPoolFixture.Entry("fx/hit", PoolPolicy.Burst, name: "low", priority: 1, group: "A"),
                GameObjectPoolFixture.Entry("fx/hit", PoolPolicy.Fixed, name: "high", priority: 5, group: "B")
            });
            try
            {
                int index = catalog.Resolve("fx/hit");
                Assert.That(catalog.GetRule(index).EntryName, Is.EqualTo("high"));
                Assert.That(catalog.GetRule(index).Policy, Is.EqualTo(PoolPolicy.Fixed));
            }
            finally { catalog.Dispose(); }
        }

        [Test]
        public void DuplicateLiteralEqualPriorityKeepsFirstInputAfterStableSort()
        {
            var catalog = PoolCompiledCatalog.Build(new[]
            {
                GameObjectPoolFixture.Entry("fx/hit", PoolPolicy.Burst, name: "first", priority: 3, group: "G"),
                GameObjectPoolFixture.Entry("fx/hit", PoolPolicy.Fixed, name: "second", priority: 3, group: "G")
            });
            try
            {
                int index = catalog.Resolve("fx/hit");
                Assert.That(catalog.GetRule(index).EntryName, Is.EqualTo("first"));
                Assert.That(catalog.GetRule(index).Policy, Is.EqualTo(PoolPolicy.Burst));
            }
            finally { catalog.Dispose(); }
        }

        [Test]
        public void LongerPathBeatsShorterWhenPriorityTiesAndGroupBreaksRemainingTies()
        {
            var catalog = PoolCompiledCatalog.Build(new[]
            {
                GameObjectPoolFixture.Entry("fx/*", PoolPolicy.Burst, name: "short", priority: 1, group: "Z"),
                GameObjectPoolFixture.Entry("fx/ui/*", PoolPolicy.Burst, name: "long", priority: 1, group: "A")
            });
            try
            {
                Assert.That(catalog.GetRule(catalog.Resolve("fx/ui/a")).EntryName, Is.EqualTo("long"));
                Assert.That(catalog.GetRule(catalog.Resolve("fx/a")).EntryName, Is.EqualTo("short"));
            }
            finally { catalog.Dispose(); }
        }

        [Test]
        public void NullEmptyAndExtensionlessEntriesAreSkippedAndEmptyCatalogResolvesNothing()
        {
            var catalog = PoolCompiledCatalog.Build(new[]
            {
                null,
                new PoolEntry { assetPath = "" },
                GameObjectPoolFixture.Entry("fx/ok")
            });
            try
            {
                Assert.That(catalog.RuleCount, Is.EqualTo(1));
                Assert.That(catalog.Resolve("fx/ok"), Is.GreaterThanOrEqualTo(0));
            }
            finally { catalog.Dispose(); }
            var empty = PoolCompiledCatalog.Build(null);
            try { Assert.That(empty.Resolve("fx/ok"), Is.EqualTo(-1)); }
            finally { empty.Dispose(); }
        }

        [Test]
        public void CompareByPriorityOrdersDescendingPriorityThenPathLengthThenGroup()
        {
            var low = GameObjectPoolFixture.Entry("aa", priority: 1, group: "b");
            var high = GameObjectPoolFixture.Entry("aa", priority: 2, group: "a");
            Assert.That(PoolEntry.CompareByPriority(high, low), Is.LessThan(0));
            Assert.That(PoolEntry.CompareByPriority(null, low), Is.GreaterThan(0));
            Assert.That(PoolEntry.CompareByPriority(low, null), Is.LessThan(0));
            Assert.That(PoolEntry.CompareByPriority(low, low), Is.Zero);
        }
    }
}
