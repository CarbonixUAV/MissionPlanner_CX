using Carbonix.Warnings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Carbonix.Tests.Warnings
{
    /// <summary>
    /// Provides recursive structural comparison for <see cref="ICondition"/> trees.
    /// </summary>
    static class ConditionAssert
    {
        public static void AreStructurallyEqual(ICondition expected, ICondition actual)
        {
            if (expected == null && actual == null) return;
            Assert.IsNotNull(expected, "Expected was null but actual was not");
            Assert.IsNotNull(actual, "Actual was null but expected was not");
            Assert.AreEqual(expected.GetType(), actual.GetType(),
                $"Type mismatch: expected {expected.GetType().Name}, got {actual.GetType().Name}");

            switch (expected)
            {
                case FieldCondition ef:
                    var af = (FieldCondition)actual;
                    Assert.AreEqual(ef.PropertyName, af.PropertyName, "PropertyName mismatch");
                    Assert.AreEqual(ef.Op, af.Op, "Op mismatch");
                    Assert.AreEqual(ef.Threshold, af.Threshold, "Threshold mismatch");
                    Assert.AreEqual(ef.ClearThreshold, af.ClearThreshold, "ClearThreshold mismatch");
                    break;

                case AndCondition ea:
                    var aa = (AndCondition)actual;
                    AreStructurallyEqual(ea.Left, aa.Left);
                    AreStructurallyEqual(ea.Right, aa.Right);
                    break;

                case OrCondition eo:
                    var ao = (OrCondition)actual;
                    AreStructurallyEqual(eo.Left, ao.Left);
                    AreStructurallyEqual(eo.Right, ao.Right);
                    break;

                case NotCondition en:
                    var an = (NotCondition)actual;
                    AreStructurallyEqual(en.Inner, an.Inner);
                    break;

                default:
                    Assert.Fail($"Unknown condition type: {expected.GetType().Name}");
                    break;
            }
        }
    }
}
