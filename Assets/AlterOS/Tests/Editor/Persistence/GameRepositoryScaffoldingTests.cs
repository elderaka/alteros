using System;
using NUnit.Framework;

namespace AlterOS.Tests.Persistence
{
    public sealed class GameRepositoryScaffoldingTests
    {
        [Test]
        public void SqliteRepositoryTypeIsAvailable()
        {
            var runtime = Array.Find(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == "AlterOS.Runtime");
            Assert.IsNotNull(runtime, "AlterOS.Runtime must be loaded before persistence tests run.");
            Assert.IsNotNull(runtime.GetType("AlterOS.Persistence.SqliteGameRepository"),
                "The game-owned SQLite repository has not been implemented.");
        }
    }
}
