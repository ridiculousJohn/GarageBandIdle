using System;

namespace RidiculousGaming.Utilities
{
    public class SingletonPropertyAttribute : Attribute
    {
        public bool DontDestroyOnLoad = true;
        public string SingletonName = null;
    }
}