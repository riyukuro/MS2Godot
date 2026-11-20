using Godot;

namespace Ms2Godot.Ui;

/// <summary>
/// A layer of world UI movies built outside <see cref="MovieDialog"/> (other players' and npcs' name tags and HP bars).
/// GFxManager::ResizeMovies (gfxmanager.cpp:158) resizes every movie on a device restore, these included, so the HUD's
/// resize gives each layer in <see cref="Group"/> the new window and the layer rescales what it has built, in place.
/// </summary>
public interface IMovieLayer
{
    /// <summary>The group the layers join while in the tree.</summary>
    public const string Group = "ms2_movie_layers";

    /// <summary>The window was resized: the new viewport scale (UiScale.For), for the built clips and the ones to come.</summary>
    void Resize(Vector2 window);
}
