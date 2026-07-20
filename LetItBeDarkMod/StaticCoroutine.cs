using UnityEngine;

namespace SWPT_PhotographerLighting
{
    public class StaticCoroutine : MonoBehaviour
    {
        private static StaticCoroutine instance;

        public static void Start(System.Collections.IEnumerator routine)
        {
            if (instance == null)
            {
                GameObject obj = new GameObject("LetItBeDark_CoroutineRunner");
                instance = obj.AddComponent<StaticCoroutine>();
                DontDestroyOnLoad(obj);
            }
            instance.StartCoroutine(routine);
        }
    }
}
