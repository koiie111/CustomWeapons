-- Install before enabling website selections in CustomWeapons and the site.
-- Team0 retains legacy global preferences; explicit2/3 override one side.
-- Weapon decoration remains in wp_player_skins, keyed by team and definition.
CREATE TABLE IF NOT EXISTS `cw_site_selections` (
  `steamid64` varchar(18) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `weapon_team` tinyint unsigned NOT NULL DEFAULT 0,
  `weapon` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  `model` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '',
  `revision` bigint NOT NULL DEFAULT 1,
  `updated_at` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`steamid64`,`weapon_team`,`weapon`),
  CONSTRAINT `cw_site_selection_team` CHECK (`weapon_team` IN (0,2,3)),
  CONSTRAINT `cw_site_selection_revision` CHECK (`revision` > 0)
) ENGINE=InnoDB;
