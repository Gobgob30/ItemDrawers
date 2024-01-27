using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace kg_ItemDrawers;
public class DrawerComponent : Container
{
    public static readonly List<DrawerComponent> AllDrawers = new List<DrawerComponent>();
    private static Sprite _defaultSprite;
    // public ZNetView m_nview { private set; get; }
    private UnityEngine.UI.Image _image;
    private TMP_Text _text;

    //UI
    private static bool ShowUI;
    private static DrawerOptions CurrentOptions;
    //

    public string CurrentPrefab
    {
        get => m_nview.m_zdo.GetString("Prefab");
        set => m_nview.m_zdo.Set("Prefab", value);
    }

    public int CurrentAmount
    {
        get => m_nview.m_zdo.GetInt("Amount");
        set => m_nview.m_zdo.Set("Amount", value);
    }

    public int PickupRange
    {
        get => m_nview.m_zdo.GetInt("PickupRange", ItemDrawers.DrawerPickupRange.Value);
        set => m_nview.m_zdo.Set("PickupRange", value);
    }

    private Color CurrentColor
    {
        get => global::Utils.Vec3ToColor(m_nview.m_zdo.GetVec3("Color", ItemDrawers.DefaultColor.Value));
        set => m_nview.m_zdo.Set("Color", global::Utils.ColorToVec3(value));
    }

    public bool ItemValid => !string.IsNullOrEmpty(CurrentPrefab) && ObjectDB.instance.m_itemByHash.ContainsKey(CurrentPrefab.GetStableHashCode());
    private int ItemMaxStack => ObjectDB.instance.m_itemByHash[CurrentPrefab.GetStableHashCode()].GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
    private string LocalizedName => ObjectDB.instance.m_itemByHash[CurrentPrefab.GetStableHashCode()].GetComponent<ItemDrop>().m_itemData.m_shared.m_name.Localize();

    private struct DrawerOptions : ISerializableParameter
    {
        public DrawerComponent drawer;
        public Color32 color;
        public int pickupRange;

        public void Serialize(ref ZPackage pkg)
        {
            pkg.Write(global::Utils.ColorToVec3(color));
            pkg.Write(pickupRange);
        }

        public void Deserialize(ref ZPackage pkg)
        {
            color = global::Utils.Vec3ToColor(pkg.ReadVector3());
            pickupRange = pkg.ReadInt();
        }
    }

    private void OnDestroy() => AllDrawers.Remove(this);
    private void Awake()
    {
        m_nview = (m_rootObjectOverride ? m_rootObjectOverride.GetComponent<ZNetView>() : GetComponent<ZNetView>());
        m_width = 1;
        m_height = 1;
        if (m_nview.GetZDO() != null)
        {
            ZLog.Log("DrawerComponent: " + m_nview.GetZDO().GetString("Prefab"));
            m_inventory = new Inventory(m_name, m_bkg, m_width, m_height);
            Inventory inventory = m_inventory;
            inventory.m_onChanged = (Action)Delegate.Combine(inventory.m_onChanged, new Action(OnContainerChanged));
            m_piece = GetComponent<Piece>();
            if ((bool)m_nview)
            {
                m_nview.Register<long>("RequestOpen", RPC_RequestOpen);
                m_nview.Register<bool>("OpenRespons", RPC_OpenRespons);
                m_nview.Register<long>("RPC_RequestStack", RPC_RequestStack);
                m_nview.Register<bool>("RPC_StackResponse", RPC_StackResponse);
                m_nview.Register<long>("RequestTakeAll", RPC_RequestTakeAll);
                m_nview.Register<bool>("TakeAllRespons", RPC_TakeAllRespons);
            }

            WearNTear wearNTear = (m_rootObjectOverride ? m_rootObjectOverride.GetComponent<WearNTear>() : GetComponent<WearNTear>());
            if ((bool)wearNTear)
            {
                wearNTear.m_onDestroyed = (Action)Delegate.Combine(wearNTear.m_onDestroyed, new Action(OnDestroyed));
            }

            Destructible destructible = (m_rootObjectOverride ? m_rootObjectOverride.GetComponent<Destructible>() : GetComponent<Destructible>());
            if ((bool)destructible)
            {
                destructible.m_onDestroyed = (Action)Delegate.Combine(destructible.m_onDestroyed, new Action(OnDestroyed));
            }

            if (m_nview.IsOwner() && !m_nview.GetZDO().GetBool(ZDOVars.s_addedDefaultItems))
            {
                AddDefaultItems();
                m_nview.GetZDO().Set(ZDOVars.s_addedDefaultItems, value: true);
            }

            InvokeRepeating("CheckForChanges", 0f, 1f);
            if (!m_nview.IsValid()) return;
            AllDrawers.Add(this);
            _image = transform.Find("Cube/Canvas/Image").GetComponent<UnityEngine.UI.Image>();
            _defaultSprite ??= _image.sprite;
            _text = transform.Find("Cube/Canvas/Text").GetComponent<TMP_Text>();
            _text.color = CurrentColor;
            m_nview.Register<string, int>("AddItem_Request", RPC_AddItem);
            m_nview.Register<string, int>("AddItem_Player", RPC_AddItem_Player);
            m_nview.Register<int>("WithdrawItem_Request", RPC_WithdrawItem_Request);
            m_nview.Register<string, int>("UpdateIcon", RPC_UpdateIcon);
            m_nview.Register<int>("ForceRemove", RPC_ForceRemove);
            m_nview.Register<DrawerOptions>("ApplyOptions", RPC_ApplyOptions);
            RPC_UpdateIcon(0, CurrentPrefab, CurrentAmount);
            InvokeRepeating(nameof(Repeat), 2.5f, 2.5f);
        }
    }

    private void RPC_ApplyOptions(long sender, DrawerOptions options)
    {
        if (m_nview.IsOwner())
        {
            CurrentColor = options.color;
            PickupRange = Mathf.Min(ItemDrawers.MaxDrawerPickupRange.Value, options.pickupRange);
        }
        _text.color = options.color;
    }

    private void RPC_ForceRemove(long sender, int amount)
    {
        amount = Mathf.Min(amount, CurrentAmount);
        CurrentAmount -= amount;
        m_nview.InvokeRPC(ZNetView.Everybody, "UpdateIcon", CurrentPrefab, CurrentAmount);
    }

    private void RPC_WithdrawItem_Request(long sender, int amount)
    {
        if (CurrentAmount <= 0 || !ItemValid)
        {
            CurrentPrefab = "";
            CurrentAmount = 0;
            m_nview.InvokeRPC(ZNetView.Everybody, "UpdateIcon", "", 0);
            return;
        }

        if (amount <= 0) return;
        amount = Mathf.Min(amount, CurrentAmount);
        CurrentAmount -= amount;
        m_nview.InvokeRPC(sender, "AddItem_Player", CurrentPrefab, amount);
        m_nview.InvokeRPC(ZNetView.Everybody, "UpdateIcon", CurrentPrefab, CurrentAmount);
    }

    private void RPC_AddItem_Player(long _, string prefab, int amount) => Utils.InstantiateItem(ZNetScene.instance.GetPrefab(prefab), amount, 1);

    private void RPC_UpdateIcon(long _, string prefab, int amount)
    {
        if (!ItemValid)
        {
            _image.sprite = _defaultSprite;
            _text.gameObject.SetActive(false);
            return;
        }
        GameObject prefabObject = ObjectDB.instance.GetItemPrefab(prefab);
        _image.sprite = prefabObject.GetComponent<ItemDrop>().m_itemData.GetIcon();
        _text.text = amount.ToString();
        _text.gameObject.SetActive(true);
        ItemDrop.ItemData d = m_inventory.GetItemAt(0, 0);
        if (d != null && d.m_dropPrefab.name == prefab)
        {
            d.m_stack = amount;
            m_inventory.Changed();
        }
        else m_inventory.AddItem(prefab, amount, 1, 0, 0, "");
    }

    private void RPC_AddItem(long sender, string prefab, int amount)
    {
        if (!m_nview.IsOwner()) return;
        if (amount <= 0) return;
        if (ItemValid && CurrentPrefab != prefab)
        {
            Utils.InstantiateAtPos(ZNetScene.instance.GetPrefab(prefab), amount, 1, transform.position + Vector3.up * 1.5f);
            return;
        }

        int newAmount = ItemValid ? (CurrentAmount + amount) : amount;
        CurrentAmount = newAmount;
        if (CurrentPrefab != prefab) CurrentPrefab = prefab;
        m_nview.InvokeRPC(ZNetView.Everybody, "UpdateIcon", prefab, newAmount);
    }

    private bool DoRepeat => Player.m_localPlayer && ItemValid && PickupRange > 0;
    private void Repeat()
    {
        if (!m_nview.IsOwner()) return;
        if (!DoRepeat) return;

        Vector3 vector = transform.position + Vector3.up;
        foreach (ItemDrop component in ItemDrop.s_instances.Where(drop => Vector3.Distance(drop.transform.position, vector) < PickupRange))
        {
            string goName = global::Utils.GetPrefabName(component.gameObject);
            if (goName != CurrentPrefab) continue;
            if (!component.CanPickup(false))
            {
                component.RequestOwn();
                continue;
            }

            Instantiate(ItemDrawers.Explosion, component.transform.position, Quaternion.identity);
            int amount = component.m_itemData.m_stack;
            component.m_nview.ClaimOwnership();
            ZNetScene.instance.Destroy(component.gameObject);
            CurrentAmount += amount;
            m_nview.InvokeRPC(ZNetView.Everybody, "UpdateIcon", CurrentPrefab, CurrentAmount);
        }
    }

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (!ItemValid) return false;

        if (user.IsCrouching())
        {
            CurrentOptions.drawer = this;
            CurrentOptions.color = CurrentColor;
            CurrentOptions.pickupRange = PickupRange;
            ShowUI = true;
            return true;
        }

        if (Input.GetKey(KeyCode.LeftAlt))
        {
            m_nview.InvokeRPC("WithdrawItem_Request", 1);
            return true;
        }

        if (Input.GetKey(KeyCode.LeftShift))
        {
            int amount = Utils.CustomCountItems(CurrentPrefab, 1);
            if (amount <= 0) return true;
            Utils.CustomRemoveItems(CurrentPrefab, amount, 1);
            m_nview.InvokeRPC("AddItem_Request", CurrentPrefab, amount);
            return true;
        }

        m_nview.InvokeRPC("WithdrawItem_Request", ItemMaxStack);
        return true;
    }


    public bool UseItem(Humanoid user, ItemDrop.ItemData item)
    {
        string dropPrefab = item.m_dropPrefab?.name;
        if (string.IsNullOrEmpty(dropPrefab)) return false;

        if ((item.IsEquipable() || item.m_shared.m_maxStackSize <= 1) && !ItemDrawers.IncludeSet.Contains(dropPrefab)) return false;

        if (!string.IsNullOrEmpty(CurrentPrefab) && CurrentPrefab != dropPrefab) return false;

        int amount = item.m_stack;
        if (amount <= 0) return false;
        user.m_inventory.RemoveItem(item);
        m_nview.InvokeRPC("AddItem_Request", dropPrefab, amount);
        return true;
    }

    public string GetHoverText()
    {
        StringBuilder sb = new StringBuilder();
        if (!ItemValid)
        {
            sb.AppendLine("<color=yellow><b>Use Hotbar to add item</b></color>");
            return sb.ToString().Localize();
        }

        if (Player.m_localPlayer.IsCrouching())
        {
            sb.AppendLine($"[<color=yellow><b>$KEY_Use</b></color>] open settings");
            return sb.ToString().Localize();
        }

        sb.AppendLine($"<color=yellow><b>{LocalizedName}</b></color> ({CurrentAmount})");
        sb.AppendLine("<color=yellow><b>Use Hotbar to add item</b></color>\n");
        if (CurrentAmount <= 0)
        {
            sb.AppendLine($"[<color=yellow><b>$KEY_Use</b></color>] or [<color=yellow><b>Left Alt + $KEY_Use</b></color>] to clear");
            sb.AppendLine($"[<color=yellow><b>Left Shift + $KEY_Use</b></color>] to deposit all <color=yellow><b>{LocalizedName}</b></color> ({Utils.CustomCountItems(CurrentPrefab, 1)})");
            return sb.ToString().Localize();
        }

        sb.AppendLine($"[<color=yellow><b>$KEY_Use</b></color>] to withdraw stack ({ItemMaxStack})");
        sb.AppendLine($"[<color=yellow><b>Left Alt + $KEY_Use</b></color>] to withdraw single item");
        sb.AppendLine($"[<color=yellow><b>Left Shift + $KEY_Use</b></color>] to deposit all <color=yellow><b>{LocalizedName}</b></color> ({Utils.CustomCountItems(CurrentPrefab, 1)})");
        return sb.ToString().Localize();
    }

    public string GetHoverName()
    {
        return "Item Drawer";
    }

    private const int windowWidth = 300;
    private const int windowHeight = 300;
    private const int halfWindowWidth = windowWidth / 2;
    private const int halfWindowHeight = windowHeight / 2;
    public static void ProcessInput()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && ShowUI)
        {
            ShowUI = false;
            Menu.instance.OnClose();
        }
    }
    public static void ProcessGUI()
    {
        if (!ShowUI) return;
        GUI.backgroundColor = Color.white;
        Rect centerOfScreen = new(Screen.width / 2f - halfWindowWidth, Screen.height / 2f - halfWindowHeight, windowWidth, windowHeight);
        GUI.Window(218102318, centerOfScreen, Window, "Item Drawer Options");
    }
    private static void Window(int id)
    {
        if (CurrentOptions.drawer == null || !CurrentOptions.drawer.m_nview.IsValid())
        {
            ShowUI = false;
            return;
        }
        GUILayout.Label($"Current Drawer: <color=yellow><b>{CurrentOptions.drawer.LocalizedName}</b></color> ({CurrentOptions.drawer.CurrentAmount})");
        byte r = CurrentOptions.color.r;
        byte g = CurrentOptions.color.g;
        byte b = CurrentOptions.color.b;
        GUILayout.Label($"Text Color: <color=#{r:X2}{g:X2}{b:X2}><b>0123456789</b></color>");
        GUILayout.Label($"R: {r}");
        r = (byte)GUILayout.HorizontalSlider(r, 0, 255);
        GUILayout.Label($"G: {g}");
        g = (byte)GUILayout.HorizontalSlider(g, 0, 255);
        GUILayout.Label($"B: {b}");
        b = (byte)GUILayout.HorizontalSlider(b, 0, 255);
        CurrentOptions.color = new Color32(r, g, b, 255);
        int pickupRange = CurrentOptions.pickupRange;
        GUILayout.Space(16f);
        GUILayout.Label($"Pickup Range: <color={(pickupRange > 0 ? "lime" : "red")}><b>{pickupRange}</b></color>");
        pickupRange = (int)GUILayout.HorizontalSlider(pickupRange, 0, ItemDrawers.MaxDrawerPickupRange.Value);
        CurrentOptions.pickupRange = pickupRange;
        GUILayout.Space(16f);
        if (GUILayout.Button("<color=lime>Apply</color>"))
        {
            CurrentOptions.drawer.m_nview.InvokeRPC(ZNetView.Everybody, "ApplyOptions", CurrentOptions);
            ShowUI = false;
        }
    }

    [HarmonyPatch]
    private static class IsVisible
    {
        [HarmonyTargetMethods, UsedImplicitly]
        private static IEnumerable<MethodInfo> Methods()
        {
            yield return AccessTools.Method(typeof(TextInput), nameof(TextInput.IsVisible));
            yield return AccessTools.Method(typeof(StoreGui), nameof(StoreGui.IsVisible));
        }

        [HarmonyPostfix, UsedImplicitly]
        private static void SetTrue(ref bool __result) => __result |= ShowUI;
    }
}

[HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
public static class Piece_OnDestroy_Patch
{
    [UsedImplicitly]
    private static void Postfix(Piece __instance)
    {
        if (__instance.gameObject.GetComponent<DrawerComponent>() is { } drawer)
        {
            if (drawer.ItemValid && drawer.CurrentAmount > 0)
            {
                Utils.InstantiateAtPos(ZNetScene.instance.GetPrefab(drawer.CurrentPrefab), drawer.CurrentAmount, 1, __instance.transform.position + Vector3.up);
            }
        }
    }
}
