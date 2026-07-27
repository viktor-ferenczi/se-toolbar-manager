using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Sandbox.Definitions;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens;
using VRage.Game;


namespace ClientPlugin.Gui;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class CustomSearchCondition : IMySearchCondition
{
    private string searchText = "";
    private readonly HashSet<MyCubeBlockDefinitionGroup> sortedBlocks = new HashSet<MyCubeBlockDefinitionGroup>();

    public string SearchName
    {
        set => searchText = value;
    }

    public bool IsValid => searchText != null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clean()
    {
        searchText = "";
        CleanDefinitionGroups();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CleanDefinitionGroups() => sortedBlocks.Clear();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HashSet<MyCubeBlockDefinitionGroup> GetSortedBlocks() => sortedBlocks;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MatchesCondition(MyDefinitionBase definition) => MatchesCondition(definition?.DisplayNameText);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddDefinitionGroup(MyCubeBlockDefinitionGroup definitionGroup) => sortedBlocks.Add(definitionGroup);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MatchesCondition(string name)
    {
        if (name == null)
            return false;

        var ss = searchText;
        var sl = ss.Length;
        if (sl == 0)
            return false;

        // Prepare the first character of the search string
        var si = 0;
        var sc = ss[0];
        var su = sc >= '0' && sc <= '9' || sc >= 'A' && sc <= 'Z';

        // Walk on each character of the name
        // Index based algorithm for speed (it does not allocate)
        var nl = name.Length;
        for (var ii = 0; ii < nl; ii++)
        {
            // Mismatching character?
            if (name[ii] != sc)
            {
                // Digits and upper case characters allow skipping characters
                if (su)
                    continue;

                // Anything else matched exactly
                break;
            }

            // Skip the matching character in the search string
            si += 1;
            if (si == sl)
                return true;

            // Recall the next character from the search string
            sc = ss[si];
            su = sc >= '0' && sc <= '9' || sc >= 'A' && sc <= 'Z';
        }

        return false;
    }
}
