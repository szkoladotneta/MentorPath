import importlib.util
from pathlib import Path
import unittest


ROOT = Path(__file__).parents[2]
spec = importlib.util.spec_from_file_location(
    "azure_deployment_preflight", ROOT / "scripts" / "azure_deployment_preflight.py")
preflight = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preflight)


class AzureDeploymentPreflightTests(unittest.TestCase):
    def test_healthy_runtime_readiness_passes(self):
        self.assertEqual(preflight.validate("Running", "200", "Healthy\n"), [])

    def test_rejects_unhealthy_or_stopped_app(self):
        self.assertTrue(preflight.validate("Stopped", "503", "Unavailable"))

class AzureDeploymentControlFlowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = (ROOT / ".github" / "workflows" / "deploy-azure.yml").read_text()
        cls.runbook = (ROOT / "docs" / "AZURE_DEPLOYMENT.md").read_text()

    def test_rejected_preflight_is_before_maintenance_and_blocks_later_actions(self):
        preflight = self.workflow.index("- name: Pre-maintenance Data Protection preflight")
        stop = self.workflow.index("- name: Stop application for maintenance")
        migrate = self.workflow.index("- name: Apply migrations once")
        update = self.workflow.index("- name: Update container and start application")
        self.assertLess(preflight, stop)
        self.assertLess(stop, migrate)
        self.assertLess(migrate, update)
        preflight_step = self.workflow[preflight:stop]
        self.assertIn("scripts/azure_deployment_preflight.py", preflight_step)
        self.assertNotIn("if: always()", preflight_step)
        post_preflight = self.workflow[stop:self.workflow.index("- name: Verify readiness and browser assets", update)]
        self.assertNotIn("if: always()", post_preflight)

    def test_backup_migration_image_update_and_exact_firewall_cleanup_order_remain(self):
        backup = self.workflow.index("- name: Save a private pre-migration database backup")
        stop = self.workflow.index("- name: Stop application for maintenance")
        migrate = self.workflow.index("- name: Apply migrations once")
        update = self.workflow.index("- name: Update container and start application")
        cleanup = self.workflow.index("- name: Remove only this run's database firewall rule")
        self.assertLess(backup, stop)
        self.assertLess(stop, migrate)
        self.assertLess(migrate, update)
        self.assertLess(update, cleanup)
        self.assertIn("steps.firewall.outputs.attempted == 'true'", self.workflow[cleanup:])
        self.assertIn("scripts/azure_firewall.py", self.workflow[cleanup:])
        self.assertIn("dotnet MentorPath.Web.dll migrate", self.workflow[migrate:update])

    def test_preflight_does_not_read_app_settings_from_azure(self):
        preflight_step = self.workflow[
            self.workflow.index("- name: Pre-maintenance Data Protection preflight"):
        ]
        self.assertNotIn("appsettings list", preflight_step)
        self.assertIn("/health/ready", preflight_step)
        self.assertNotIn("DataProtection__CertificatePassword", preflight_step)
        health_check = (ROOT / "src" / "MentorPath.Web" / "Composition" / "DataProtectionKeyRingHealthCheck.cs").read_text()
        self.assertIn("DataProtection:CertificatePath", health_check)
        self.assertIn("DataProtection:BlobUri", health_check)

    def test_runbook_has_explicit_pre_migration_partial_and_post_update_recovery_branches(self):
        for branch in ("Proven failure before migration", "Migration may have started or partially applied", "Image updated but startup/readiness failed"):
            self.assertIn(branch, self.runbook)
        self.assertIn("does not automatically restart", self.runbook)
        self.assertIn("password-setup/reset-token", self.runbook)
        self.assertIn("isolated PostgreSQL 17", self.runbook)


if __name__ == "__main__":
    unittest.main()
