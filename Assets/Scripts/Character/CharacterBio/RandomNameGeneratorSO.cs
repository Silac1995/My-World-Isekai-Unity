using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewNameGenerator", menuName = "Character/Name Generator")]
public class RandomNameGeneratorSO : ScriptableObject, INameGenerator
{
    [SerializeField] private List<string> _maleNames = new List<string>();
    [SerializeField] private List<string> _femaleNames = new List<string>();
    [SerializeField] private List<string> _neutralNames = new List<string>();

    public string GenerateName(GenderType gender)
    {
        List<string> selectedList = _neutralNames;

        if (gender == GenderType.Male && _maleNames.Count > 0)
        {
            selectedList = _maleNames;
        }
        else if (gender == GenderType.Female && _femaleNames.Count > 0)
        {
            selectedList = _femaleNames;
        }
        else if (selectedList == null || selectedList.Count == 0)
        {
            // Fallback to whichever list has items
            if (_maleNames != null && _maleNames.Count > 0) selectedList = _maleNames;
            else if (_femaleNames != null && _femaleNames.Count > 0) selectedList = _femaleNames;
        }

        if (selectedList != null && selectedList.Count > 0)
        {
            int randomIndex = Random.Range(0, selectedList.Count);
            return selectedList[randomIndex];
        }

        return "Unknown Entity";
    }
}
