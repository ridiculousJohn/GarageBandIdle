using UnityEngine;

namespace RidiculousGaming.GarageBandIdle.Economy
{
    // Pure state: a currency is {id, balance} and does not know how it is earned
    // (design doc 12.2). Durability is which scope declares it - there is no
    // lifetime field here. Tags carry the economy vocabulary ("income" is the
    // global-income tag, declared by income currencies, 12.2).
    [CreateAssetMenu(menuName = "Garage Band Idle/Currency")]
    public class CurrencyDefinition : Definition
    {
    }
}
