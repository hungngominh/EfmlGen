using System.Collections.Generic;
using System.Linq;

namespace EfmlGen.Core;

/// <summary>
/// Decompose &lt;associations&gt; thành navigation properties trên từng class.
/// Quy ước (verify từ sample Department.cs):
/// - Class ở end1 nhận nav: name=end2.Name, multiplicity=end2.Multiplicity, type=end2.ClassName
/// - Class ở end2 nhận nav: name=end1.Name, multiplicity=end1.Multiplicity, type=end1.ClassName
/// - Self-ref (end1.Class == end2.Class): cùng class nhận 2 nav, end2-derived (collection) trước.
/// </summary>
public static class AssociationLayout
{
    public sealed class Nav
    {
        public string Name { get; init; } = "";
        public string TargetClass { get; init; } = "";
        public bool IsCollection { get; init; }
    }

    public static Dictionary<string, List<Nav>> Build(EfmlModel model)
    {
        var navsByClass = new Dictionary<string, List<Nav>>();
        foreach (var c in model.Classes)
            navsByClass[c.Name] = new List<Nav>();

        foreach (var a in model.Associations)
        {
            // Nav on end1.class (derived from end2 description)
            if (navsByClass.TryGetValue(a.End1.ClassName, out var list1))
            {
                list1.Add(new Nav
                {
                    Name = a.End2.Name,
                    TargetClass = a.End2.ClassName,
                    IsCollection = a.End2.Multiplicity == Multiplicity.Many
                });
            }

            // Nav on end2.class (derived from end1 description)
            if (navsByClass.TryGetValue(a.End2.ClassName, out var list2))
            {
                list2.Add(new Nav
                {
                    Name = a.End1.Name,
                    TargetClass = a.End1.ClassName,
                    IsCollection = a.End1.Multiplicity == Multiplicity.Many
                });
            }
        }

        return navsByClass;
    }
}
