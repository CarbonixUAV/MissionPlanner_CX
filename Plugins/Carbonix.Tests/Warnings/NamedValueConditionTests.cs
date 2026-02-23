using System.Collections.Concurrent;
using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.Warnings
{
    [TestClass]
    public class NamedValueConditionTests
    {
        [TestMethod]
        public void Evaluate_ValueInStore_ComparesCorrectly()
        {
            var store = new ConcurrentDictionary<string, float>();
            store["VTOLState"] = 1.0f;

            var cond = Condition.NamedValue("VTOLState", CompareOp.GT, 0, store);

            Assert.IsTrue(cond.Evaluate(new object()));
        }

        [TestMethod]
        public void Evaluate_ValueNotInStore_ReturnsFalse()
        {
            var store = new ConcurrentDictionary<string, float>();

            var cond = Condition.NamedValue("VTOLState", CompareOp.GT, 0, store);

            Assert.IsFalse(cond.Evaluate(new object()));
        }

        [TestMethod]
        public void Evaluate_NullStore_ReturnsFalse()
        {
            var cond = Condition.NamedValue("VTOLState", CompareOp.GT, 0);

            Assert.IsFalse(cond.Evaluate(new object()));
        }

        [TestMethod]
        public void Evaluate_EqualOp_MatchesExactValue()
        {
            var store = new ConcurrentDictionary<string, float>();
            store["state"] = 2.0f;

            var cond = Condition.NamedValue("state", CompareOp.EQ, 2.0, store);

            Assert.IsTrue(cond.Evaluate(new object()));
        }

        [TestMethod]
        public void Evaluate_LessThanOp()
        {
            var store = new ConcurrentDictionary<string, float>();
            store["temp"] = 50.0f;

            var below = Condition.NamedValue("temp", CompareOp.LT, 100, store);
            var above = Condition.NamedValue("temp", CompareOp.LT, 25, store);

            Assert.IsTrue(below.Evaluate(new object()));
            Assert.IsFalse(above.Evaluate(new object()));
        }

        [TestMethod]
        public void Evaluate_ValueUpdates_ReflectsNewValue()
        {
            var store = new ConcurrentDictionary<string, float>();
            var cond = Condition.NamedValue("VTOLState", CompareOp.GT, 0, store);

            Assert.IsFalse(cond.Evaluate(new object()));

            store["VTOLState"] = 1.0f;
            Assert.IsTrue(cond.Evaluate(new object()));

            store["VTOLState"] = 0.0f;
            Assert.IsFalse(cond.Evaluate(new object()));
        }

        [TestMethod]
        public void Store_CanBeSetAfterConstruction()
        {
            var cond = Condition.NamedValue("VTOLState", CompareOp.GT, 0);
            Assert.IsFalse(cond.Evaluate(new object()));

            var store = new ConcurrentDictionary<string, float>();
            store["VTOLState"] = 1.0f;
            cond.Store = store;

            Assert.IsTrue(cond.Evaluate(new object()));
        }
    }
}
