using Godot;
using RedAlert.Simulation;

namespace RedAlert;

public partial class Main
{
    void InitClassicRuntime()
    {
        var catalog=ClassicMapCatalog.Parse(Godot.FileAccess.GetFileAsString(ClassicMapCatalog.ResourcePath));
        ClassicMapRepository.Loader=id=>
        {
            var info=catalog.Find(id)??throw new InvalidDataException($"未知原版地图：{id}");
            var definition=ClassicMapDefinition.Parse(Godot.FileAccess.GetFileAsString(info.DataPath));
            if(definition.SourceSha256!=info.SourceSha256 || definition.Width!=info.Width || definition.Height!=info.Height)
                throw new InvalidDataException("原版地图与目录内容不一致");
            return definition;
        };
    }
}
