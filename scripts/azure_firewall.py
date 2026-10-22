"""Delete one deployment firewall rule and verify ARM's eventual result."""

import argparse
import json
import subprocess
import time
from urllib.parse import urlsplit, urlunsplit


def request(method, url):
    try:
        result = subprocess.run(
            ["az", "rest", "--method", method, "--url", url, "--output", "json"],
            capture_output=True, text=True, timeout=45,
        )
    except subprocess.TimeoutExpired:
        return None, "RequestTimeout"
    if result.returncode:
        error = result.stderr.strip()
        transient = (
            "GatewayTimeout", "InternalServerError", "ServiceUnavailable",
            "TooManyRequests", "RequestTimeout", "Conflict",
        )
        if any(code in error for code in transient):
            return None, error
        if method == "delete" and ("ResourceNotFound" in error or "NotFound" in error):
            return {}, None
        raise RuntimeError(error or "Azure CLI failed without an error message.")
    return json.loads(result.stdout) if result.stdout.strip() else {}, None


def delete_rule(url, attempts=6, delay=10):
    parsed = urlsplit(url)
    collection, rule = parsed.path.rsplit("/", 1)
    if (parsed.scheme != "https" or parsed.netloc != "management.azure.com"
            or not collection.endswith("/firewallRules") or not rule.startswith("github-")):
        raise ValueError("Expected an ARM URL for one github-* firewall rule.")
    list_url = urlunsplit(parsed._replace(path=collection))
    last_error = ""
    for attempt in range(attempts):
        # Always reconcile after DELETE: a timeout can mean Azure accepted it.
        _, delete_error = request("delete", url)
        rules, list_error = request("get", list_url)
        if rules is not None:
            if "value" not in rules or rules.get("nextLink"):
                raise RuntimeError("Incomplete firewall inventory; cannot verify deletion.")
            if not any(item["name"] == rule for item in rules["value"]):
                print(f"Verified firewall rule absent: {rule}")
                return
        last_error = list_error or delete_error or "Rule still present after deletion request."
        print(f"Firewall cleanup pending ({attempt + 1}/{attempts}): {last_error}", flush=True)
        if attempt + 1 < attempts:
            time.sleep(delay)
    raise RuntimeError(f"Could not verify deletion of {rule}: {last_error}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("url")
    args = parser.parse_args()
    delete_rule(args.url)
