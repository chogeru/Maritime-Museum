
using UnityEngine;

/// <summary>
/// Plain (non-UdonSharp) MonoBehaviour that just holds a reference to a
/// PresentationTourLibrary asset for editor authoring convenience. See RevealTourLibraryRef
/// for why this can't live directly on the UdonSharpBehaviour.
/// </summary>
public class PresentationTourLibraryRef : MonoBehaviour
{
    public PresentationTourLibrary library;
}
