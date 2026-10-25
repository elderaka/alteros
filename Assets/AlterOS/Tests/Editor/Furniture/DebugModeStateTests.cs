using System.Collections.Generic;
using NUnit.Framework;
using AlterOS.Debugging;

namespace AlterOS.Tests.Furniture
{
    public sealed class DebugModeStateTests
    {
        [Test]
        public void StateStartsOffAndNotifiesOnlyWhenValueChanges()
        {
            var state = new DebugModeState();
            var changes = new List<bool>();
            state.Changed += changes.Add;

            Assert.IsFalse(state.IsEnabled);
            state.SetEnabled(false);
            state.SetEnabled(true);
            state.SetEnabled(true);
            state.Toggle();

            Assert.IsFalse(state.IsEnabled);
            CollectionAssert.AreEqual(new[] { true, false }, changes);
        }
    }
}
