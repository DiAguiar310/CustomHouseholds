using Colossal.UI.Binding;
using Game.UI.InGame;

namespace CustomHouseholds.Systems
{
    public class HouseholdOverrideSection : ISectionSource, IJsonWritable
    {
        public const string kTypeName = "CustomHouseholds.HouseholdOverrideSection";

        public void RequestUpdate()
        {
        }

        public void PerformUpdate()
        {
        }

        public void Write(IJsonWriter writer)
        {
            writer.TypeBegin(kTypeName);

            writer.PropertyName("group");
            writer.Write("CustomHouseholds");

            writer.PropertyName("tooltipKeys");
            writer.ArrayBegin(0);
            writer.ArrayEnd();

            writer.PropertyName("tooltipTags");
            writer.ArrayBegin(0);
            writer.ArrayEnd();

            writer.TypeEnd();
        }
    }
}
