using UnityEngine;

namespace AranciaAssets.EditorTools {

	public abstract class AranciaBehaviour : MonoBehaviour {
		[HideInInspector]
		[SerializeField]
		internal string AranciaComment;
	}
}