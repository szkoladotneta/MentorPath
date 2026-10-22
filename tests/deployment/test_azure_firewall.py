import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch
import subprocess


spec = importlib.util.spec_from_file_location(
    "azure_firewall", Path(__file__).parents[2] / "scripts" / "azure_firewall.py")
firewall = importlib.util.module_from_spec(spec)
spec.loader.exec_module(firewall)
URL = ("https://management.azure.com/subscriptions/test/resourceGroups/test/providers/"
       "Microsoft.DBforPostgreSQL/flexibleServers/test/firewallRules/github-123-1"
       "?api-version=2024-08-01")
PRESENT = {"value": [{"name": "github-123-1"}, {"name": "app-outbound-1"}]}
ABSENT = {"value": [{"name": "app-outbound-1"}]}


class FirewallCleanupTests(unittest.TestCase):
    def test_timeout_with_completed_delete_succeeds(self):
        with patch.object(firewall, "request", side_effect=[
            (None, "GatewayTimeout"), (ABSENT, None),
        ]) as request:
            firewall.delete_rule(URL, delay=0)
        self.assertEqual(request.call_count, 2)
        self.assertEqual(request.call_args_list[1].args[0], "get")

    def test_eventually_deleted_rule_retries_same_url(self):
        with patch.object(firewall, "request", side_effect=[
            ({}, None), (PRESENT, None), ({}, None), (ABSENT, None),
        ]) as request:
            firewall.delete_rule(URL, delay=0)
        self.assertEqual(request.call_args_list[2].args, ("delete", URL))

    def test_inventory_timeout_is_not_success(self):
        with patch.object(firewall, "request", side_effect=[
            ({}, None), (None, "GatewayTimeout"),
        ]):
            with self.assertRaisesRegex(RuntimeError, "Could not verify"):
                firewall.delete_rule(URL, attempts=1, delay=0)

    def test_persistent_rule_is_failure(self):
        with patch.object(firewall, "request", side_effect=[({}, None), (PRESENT, None)]):
            with self.assertRaisesRegex(RuntimeError, "Could not verify"):
                firewall.delete_rule(URL, attempts=1, delay=0)

    def test_authorization_error_fails_without_retry(self):
        with patch.object(firewall, "request", side_effect=RuntimeError("AuthorizationFailed")):
            with self.assertRaisesRegex(RuntimeError, "AuthorizationFailed"):
                firewall.delete_rule(URL, delay=0)

    def test_pagination_cannot_falsely_confirm_absence(self):
        with patch.object(firewall, "request", side_effect=[
            ({}, None), ({"value": [], "nextLink": "more"}, None),
        ]):
            with self.assertRaisesRegex(RuntimeError, "Incomplete"):
                firewall.delete_rule(URL, delay=0)

    def test_non_runner_rule_rejected(self):
        with self.assertRaises(ValueError):
            firewall.delete_rule(URL.replace("github-123-1", "app-outbound-1"))

    def test_cli_timeout_is_retryable(self):
        with patch.object(firewall.subprocess, "run", side_effect=subprocess.TimeoutExpired("az", 45)):
            self.assertEqual(firewall.request("delete", URL), (None, "RequestTimeout"))

    def test_gateway_error_is_retryable(self):
        result = subprocess.CompletedProcess([], 1, "", "ERROR: GatewayTimeout")
        with patch.object(firewall.subprocess, "run", return_value=result):
            self.assertEqual(firewall.request("delete", URL), (None, "ERROR: GatewayTimeout"))

    def test_cli_authorization_error_is_not_swallowed(self):
        result = subprocess.CompletedProcess([], 1, "", "ERROR: AuthorizationFailed")
        with patch.object(firewall.subprocess, "run", return_value=result):
            with self.assertRaisesRegex(RuntimeError, "AuthorizationFailed"):
                firewall.request("delete", URL)


if __name__ == "__main__":
    unittest.main()
