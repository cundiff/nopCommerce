#!/bin/bash
# Cursor CLI equivalent of demo-test.sh (uses `agent`, not the SDK).
# Requires: agent CLI logged in (`agent login`) or CURSOR_API_KEY set.
set -e

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$REPO_ROOT"
mkdir -p demo-outputs

TEST_Q="In the nopCommerce codebase, show me all overloads of GetFinalPriceAsync on IPriceCalculationService. For each overload give me the complete signature, exact parameter names, types, default values, and the exact return type."

# ask = read-only Q&A; --trust = headless workspace trust
AGENT=(agent -p --mode ask --trust --workspace "$REPO_ROOT")

WARMUPS=(
  "Give me an overview of the nopCommerce project structure. What are the main projects in the solution and what does each one own?"
  "How does the plugin system work? If I wanted to write a plugin that adds a new payment method, what interfaces do I need to implement and how does nopCommerce discover and load it at runtime?"
  "Explain the caching infrastructure. What's the difference between IStaticCacheManager and IDynamicCacheManager? How do cache keys get constructed and how is cache invalidation handled when an entity changes?"
  "Trace the order placement flow from when a customer clicks Place Order. Walk through the method calls in IOrderProcessingService, what validation happens, and how the order entity gets created and persisted."
  "How does customer authentication work? Walk through the relationship between ICustomerService, IAuthenticationService, and the ASP.NET Core middleware. What happens when a returning customer logs in?"
  "Explain the product attribute system. How are configurable products like size and color combinations modeled in the data layer? How does IProductAttributeService relate to IProductAttributeParser?"
  "How is tax calculated on shipping costs specifically? Which service handles this, and does the calculation differ based on tax display type or customer tax exemption status?"
  "How does multi-store work? If I have two storefronts sharing a product catalog, how does the application know which store settings to apply? Where does IStoreContext fit in?"
  "How does the discount engine work? If a customer has both a percentage coupon code and a product already on a category discount, how does the system decide which discounts apply and in what order?"
  "What is the difference between IWorkContext and IStoreContext? Give me concrete examples of when I would use one versus the other."
  "How does the scheduled task system work? If I want to add a background job that runs every hour to sync inventory from an external system, what do I implement and how does nopCommerce schedule it?"
  "Explain the localization system. How are string resources stored, how does the application select the right locale, and what is the recommended way to add a new localizable string to a plugin?"
)

echo "=== COLD BASELINE (Cursor CLI) ===" && echo ""
"${AGENT[@]}" "$TEST_Q" | tee demo-outputs/cursor_baseline.txt
echo ""

echo "Starting warm-up sequence..."
"${AGENT[@]}" "${WARMUPS[0]}" > demo-outputs/cursor_warmup_01.txt
echo "1/12 done"

for i in "${!WARMUPS[@]}"; do
  if [ "$i" -gt 0 ]; then
    PADDED=$(printf "%02d" $((i+1)))
    agent -p --continue --mode ask --trust --workspace "$REPO_ROOT" "${WARMUPS[$i]}" > "demo-outputs/cursor_warmup_${PADDED}.txt"
    echo "$((i+1))/12 done"
    sleep 1
  fi
done

echo "" && echo "=== POST-WARMUP (Cursor CLI) ===" && echo ""
agent -p --continue --mode ask --trust --workspace "$REPO_ROOT" "$TEST_Q" | tee demo-outputs/cursor_result.txt

echo "" && echo "=== COMPARISON ===" && echo ""
echo "BASELINE:" && cat demo-outputs/cursor_baseline.txt
echo "" && echo "POST-WARMUP:" && cat demo-outputs/cursor_result.txt
