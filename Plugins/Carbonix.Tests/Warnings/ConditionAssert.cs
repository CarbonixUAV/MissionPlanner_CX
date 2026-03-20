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
                case CompareCondition ec:
                    var ac = (CompareCondition)actual;
                    Assert.AreEqual(ec.ValueSource, ac.ValueSource, "ValueSource mismatch");
                    Assert.AreEqual(ec.Name, ac.Name, "Name mismatch");
                    Assert.AreEqual(ec.Op, ac.Op, "Op mismatch");
                    Assert.AreEqual(ec.Threshold, ac.Threshold, "Threshold mismatch");
                    Assert.AreEqual(ec.ClearThreshold, ac.ClearThreshold, "ClearThreshold mismatch");
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

                case StatusTextCondition es:
                    var asst = (StatusTextCondition)actual;
                    Assert.AreEqual(es.FirePattern.ToString(), asst.FirePattern.ToString(),
                        "FirePattern mismatch");
                    Assert.AreEqual(es.ClearPattern?.ToString(), asst.ClearPattern?.ToString(),
                        "ClearPattern mismatch");
                    Assert.AreEqual(es.TimeoutMs, asst.TimeoutMs, "TimeoutMs mismatch");
                    break;

                case EdgeCondition ee:
                    var ae = (EdgeCondition)actual;
                    AreStructurallyEqual(ee.Inner, ae.Inner);
                    break;

                case LatchCondition el:
                    var al = (LatchCondition)actual;
                    AreStructurallyEqual(el.Set, al.Set);
                    AreStructurallyEqual(el.Clear, al.Clear);
                    break;

                default:
                    Assert.Fail($"Unknown condition type: {expected.GetType().Name}");
                    break;
            }
        }
    }
}
