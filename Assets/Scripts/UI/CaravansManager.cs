using UnityEngine;

[RequireComponent(typeof(Hover))]
public class CaravansManager : MonoBehaviour
{
    public ProducesEnum produce;
    public int fontSize = 25;
    private string caravanPricesString;

    void OnEnable()
    {
        StoresManager storesManager = FindAnyObjectByType<StoresManager>();
        if (storesManager != null) storesManager.MarketChanged += RefreshCaravanPrices;
        GetCaravanPrices();
    }

    void OnDisable()
    {
        StoresManager storesManager = FindAnyObjectByType<StoresManager>();
        if (storesManager != null) storesManager.MarketChanged -= RefreshCaravanPrices;
    }

    private void RefreshCaravanPrices()
    {
        GetCaravanPrices();
    }

    public string GetCaravanPrices()
    {
        StoresManager storesManager = FindAnyObjectByType<StoresManager>();
        if (storesManager == null) return caravanPricesString;

        int buildPrice = storesManager.GetBuyPricePerUnit(produce);
        int sellPrice = storesManager.GetSellPricePerUnit(produce);
        string spriteName = produce.ToString().ToLowerInvariant();
        string produceName = char.ToUpperInvariant(spriteName[0]) + spriteName[1..];

        caravanPricesString = $"<sprite name=\"{spriteName}\">{produceName}\nBuild: {buildPrice}\nSell: {sellPrice}";

        Hover hover = GetComponent<Hover>();
        if (hover != null) hover.Initialize(caravanPricesString, fontSize);

        return caravanPricesString;
    }
}
