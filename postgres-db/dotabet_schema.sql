--
-- PostgreSQL database dump
--

-- Dumped from database version 14.6
-- Dumped by pg_dump version 14.6

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: Kali; Type: SCHEMA; Schema: -; Owner: machine_user
--

CREATE ROLE machine_user WITH SUPERUSER;

CREATE SCHEMA "Kali";


ALTER SCHEMA "Kali" OWNER TO machine_user;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: ability_details; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".ability_details (
    match_id bigint NOT NULL,
    player_slot integer NOT NULL,
    ability integer NOT NULL,
    "time" integer NOT NULL,
    level integer NOT NULL
);


ALTER TABLE "Kali".ability_details OWNER TO machine_user;

--
-- Name: announce_message; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".announce_message (
    lobby_id bigint NOT NULL,
    message_id bigint NOT NULL
);


ALTER TABLE "Kali".announce_message OWNER TO machine_user;

--
-- Name: announce_message_lobby_id_seq; Type: SEQUENCE; Schema: Kali; Owner: machine_user
--

CREATE SEQUENCE "Kali".announce_message_lobby_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE "Kali".announce_message_lobby_id_seq OWNER TO machine_user;

--
-- Name: announce_message_lobby_id_seq; Type: SEQUENCE OWNED BY; Schema: Kali; Owner: machine_user
--

ALTER SEQUENCE "Kali".announce_message_lobby_id_seq OWNED BY "Kali".announce_message.lobby_id;


--
-- Name: balance_ledger; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".balance_ledger (
    discord_id bigint NOT NULL,
    tokens bigint NOT NULL
);


ALTER TABLE "Kali".balance_ledger OWNER TO machine_user;

--
-- Name: balance_ledger_discord_id_seq; Type: SEQUENCE; Schema: Kali; Owner: machine_user
--

CREATE SEQUENCE "Kali".balance_ledger_discord_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE "Kali".balance_ledger_discord_id_seq OWNER TO machine_user;

--
-- Name: balance_ledger_discord_id_seq; Type: SEQUENCE OWNED BY; Schema: Kali; Owner: machine_user
--

ALTER SEQUENCE "Kali".balance_ledger_discord_id_seq OWNED BY "Kali".balance_ledger.discord_id;


--
-- Name: bear_details; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".bear_details (
    match_id bigint NOT NULL,
    player_slot integer NOT NULL,
    unitname character varying NOT NULL,
    item_0 integer NOT NULL,
    item_1 integer NOT NULL,
    item_2 integer NOT NULL,
    item_3 integer NOT NULL,
    item_4 integer NOT NULL,
    item_5 integer NOT NULL,
    backpack_0 integer NOT NULL,
    backpack_1 integer NOT NULL,
    backpack_2 integer NOT NULL,
    item_neutral integer NOT NULL
);


ALTER TABLE "Kali".bear_details OWNER TO machine_user;

--
-- Name: bet_ledger; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".bet_ledger (
    wager_id integer NOT NULL,
    match_id bigint NOT NULL,
    gambler_id bigint NOT NULL,
    side integer NOT NULL,
    amount bigint NOT NULL,
    finalized boolean NOT NULL
);


ALTER TABLE "Kali".bet_ledger OWNER TO machine_user;

--
-- Name: bet_ledger_wager_id_seq; Type: SEQUENCE; Schema: Kali; Owner: machine_user
--

CREATE SEQUENCE "Kali".bet_ledger_wager_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE "Kali".bet_ledger_wager_id_seq OWNER TO machine_user;

--
-- Name: bet_ledger_wager_id_seq; Type: SEQUENCE OWNED BY; Schema: Kali; Owner: machine_user
--

ALTER SEQUENCE "Kali".bet_ledger_wager_id_seq OWNED BY "Kali".bet_ledger.wager_id;


--
-- Name: charity; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".charity (
    charity_id integer NOT NULL,
    wager_id integer,
    gambler_id bigint NOT NULL,
    amount bigint NOT NULL,
    reason character varying NOT NULL,
    query_time bigint NOT NULL
);


ALTER TABLE "Kali".charity OWNER TO machine_user;

--
-- Name: charity_charity_id_seq; Type: SEQUENCE; Schema: Kali; Owner: machine_user
--

CREATE SEQUENCE "Kali".charity_charity_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE "Kali".charity_charity_id_seq OWNER TO machine_user;

--
-- Name: charity_charity_id_seq; Type: SEQUENCE OWNED BY; Schema: Kali; Owner: machine_user
--

ALTER SEQUENCE "Kali".charity_charity_id_seq OWNED BY "Kali".charity.charity_id;


--
-- Name: discord_ids; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".discord_ids (
    discord_id bigint NOT NULL,
    steam_id bigint NOT NULL,
    account_id bigint NOT NULL
);


ALTER TABLE "Kali".discord_ids OWNER TO machine_user;

--
-- Name: extended_match_details_request; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".extended_match_details_request (
    match_id bigint NOT NULL,
    request_time integer NOT NULL,
    match_details_retrieved boolean NOT NULL
);


ALTER TABLE "Kali".extended_match_details_request OWNER TO machine_user;

--
-- Name: extended_match_details_request_match_id_seq; Type: SEQUENCE; Schema: Kali; Owner: machine_user
--

CREATE SEQUENCE "Kali".extended_match_details_request_match_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE "Kali".extended_match_details_request_match_id_seq OWNER TO machine_user;

--
-- Name: extended_match_details_request_match_id_seq; Type: SEQUENCE OWNED BY; Schema: Kali; Owner: machine_user
--

ALTER SEQUENCE "Kali".extended_match_details_request_match_id_seq OWNED BY "Kali".extended_match_details_request.match_id;


--
-- Name: friends; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".friends (
    steam_id bigint NOT NULL
);


ALTER TABLE "Kali".friends OWNER TO machine_user;

--
-- Name: live_lobbies; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".live_lobbies (
    lobby_id bigint NOT NULL
);


ALTER TABLE "Kali".live_lobbies OWNER TO machine_user;

--
-- Name: live_matches; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".live_matches (
    query_time bigint NOT NULL,
    activate_time bigint NOT NULL,
    deactivate_time bigint NOT NULL,
    server_steam_id bigint NOT NULL,
    lobby_id bigint NOT NULL,
    league_id integer NOT NULL,
    lobby_type integer NOT NULL,
    game_time integer NOT NULL,
    delay integer NOT NULL,
    spectators integer NOT NULL,
    game_mode integer NOT NULL,
    average_mmr integer NOT NULL,
    match_id bigint NOT NULL,
    series_id integer NOT NULL,
    sort_score integer NOT NULL,
    last_update_time double precision NOT NULL,
    radiant_lead integer NOT NULL,
    radiant_score integer NOT NULL,
    dire_score integer NOT NULL,
    building_state integer NOT NULL,
    custom_game_difficulty integer
);


ALTER TABLE "Kali".live_matches OWNER TO machine_user;

--
-- Name: live_players; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".live_players (
    match_id bigint NOT NULL,
    player_num integer NOT NULL,
    account_id bigint NOT NULL,
    hero_id integer NOT NULL
);


ALTER TABLE "Kali".live_players OWNER TO machine_user;

--
-- Name: lobby_message; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".lobby_message (
    lobby_id bigint NOT NULL,
    message_id bigint NOT NULL
);


ALTER TABLE "Kali".lobby_message OWNER TO machine_user;

--
-- Name: lobby_message_lobby_id_seq; Type: SEQUENCE; Schema: Kali; Owner: machine_user
--

CREATE SEQUENCE "Kali".lobby_message_lobby_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE "Kali".lobby_message_lobby_id_seq OWNER TO machine_user;

--
-- Name: lobby_message_lobby_id_seq; Type: SEQUENCE OWNED BY; Schema: Kali; Owner: machine_user
--

ALTER SEQUENCE "Kali".lobby_message_lobby_id_seq OWNED BY "Kali".lobby_message.lobby_id;


--
-- Name: match_details; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".match_details (
    match_id bigint NOT NULL,
    duration integer NOT NULL,
    pre_game_duration integer NOT NULL,
    start_time bigint NOT NULL,
    match_seq_num bigint NOT NULL,
    tower_status_radiant integer NOT NULL,
    tower_status_dire integer NOT NULL,
    barracks_status_radiant integer NOT NULL,
    barracks_status_dire integer NOT NULL,
    cluster integer NOT NULL,
    first_blood_time integer NOT NULL,
    lobby_type integer NOT NULL,
    human_players integer NOT NULL,
    leagueid integer NOT NULL,
    positive_votes integer NOT NULL,
    negative_votes integer NOT NULL,
    game_mode integer NOT NULL,
    flags integer NOT NULL,
    engine integer NOT NULL,
    radiant_score integer NOT NULL,
    dire_score integer NOT NULL,
    radiant_win boolean
);


ALTER TABLE "Kali".match_details OWNER TO machine_user;

--
-- Name: match_details_match_id_seq; Type: SEQUENCE; Schema: Kali; Owner: machine_user
--

CREATE SEQUENCE "Kali".match_details_match_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE "Kali".match_details_match_id_seq OWNER TO machine_user;

--
-- Name: match_details_match_id_seq; Type: SEQUENCE OWNED BY; Schema: Kali; Owner: machine_user
--

ALTER SEQUENCE "Kali".match_details_match_id_seq OWNED BY "Kali".match_details.match_id;


--
-- Name: match_status; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".match_status (
    match_id bigint NOT NULL,
    status integer NOT NULL
);


ALTER TABLE "Kali".match_status OWNER TO machine_user;

--
-- Name: match_status_match_id_seq; Type: SEQUENCE; Schema: Kali; Owner: machine_user
--

CREATE SEQUENCE "Kali".match_status_match_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER TABLE "Kali".match_status_match_id_seq OWNER TO machine_user;

--
-- Name: match_status_match_id_seq; Type: SEQUENCE OWNED BY; Schema: Kali; Owner: machine_user
--

ALTER SEQUENCE "Kali".match_status_match_id_seq OWNED BY "Kali".match_status.match_id;


--
-- Name: pick_details; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".pick_details (
    match_id bigint NOT NULL,
    is_pick boolean NOT NULL,
    hero_id integer NOT NULL,
    team integer NOT NULL,
    "order" integer NOT NULL
);


ALTER TABLE "Kali".pick_details OWNER TO machine_user;

--
-- Name: player_match_details; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".player_match_details (
    match_id bigint NOT NULL,
    account_id bigint NOT NULL,
    player_slot integer NOT NULL,
    hero_id integer NOT NULL,
    item_0 integer NOT NULL,
    item_1 integer NOT NULL,
    item_2 integer NOT NULL,
    item_3 integer NOT NULL,
    item_4 integer NOT NULL,
    item_5 integer NOT NULL,
    backpack_0 integer NOT NULL,
    backpack_1 integer NOT NULL,
    backpack_2 integer NOT NULL,
    item_neutral integer NOT NULL,
    kills integer NOT NULL,
    deaths integer NOT NULL,
    assists integer NOT NULL,
    leaver_status integer NOT NULL,
    last_hits integer NOT NULL,
    denies integer NOT NULL,
    gold_per_min integer NOT NULL,
    xp_per_min integer NOT NULL,
    level integer NOT NULL,
    hero_damage integer NOT NULL,
    tower_damage integer NOT NULL,
    hero_healing integer NOT NULL,
    gold integer NOT NULL,
    gold_spent integer NOT NULL,
    scaled_hero_damage integer NOT NULL,
    scaled_tower_damage integer NOT NULL,
    scaled_hero_healing integer NOT NULL,
    aghanims_scepter integer,
    aghanims_shard integer,
    moonshard integer,
    net_worth bigint,
    team_number integer,
    team_slot integer
);


ALTER TABLE "Kali".player_match_details OWNER TO machine_user;

--
-- Name: rp_heroes; Type: TABLE; Schema: Kali; Owner: machine_user
--

CREATE TABLE "Kali".rp_heroes (
    lobby_id bigint NOT NULL,
    steam_id bigint NOT NULL,
    param0 character varying NOT NULL,
    param1 character varying NOT NULL,
    param2 character varying
);


ALTER TABLE "Kali".rp_heroes OWNER TO machine_user;

--
-- Name: announce_message lobby_id; Type: DEFAULT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".announce_message ALTER COLUMN lobby_id SET DEFAULT nextval('"Kali".announce_message_lobby_id_seq'::regclass);


--
-- Name: balance_ledger discord_id; Type: DEFAULT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".balance_ledger ALTER COLUMN discord_id SET DEFAULT nextval('"Kali".balance_ledger_discord_id_seq'::regclass);


--
-- Name: bet_ledger wager_id; Type: DEFAULT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".bet_ledger ALTER COLUMN wager_id SET DEFAULT nextval('"Kali".bet_ledger_wager_id_seq'::regclass);


--
-- Name: charity charity_id; Type: DEFAULT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".charity ALTER COLUMN charity_id SET DEFAULT nextval('"Kali".charity_charity_id_seq'::regclass);


--
-- Name: extended_match_details_request match_id; Type: DEFAULT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".extended_match_details_request ALTER COLUMN match_id SET DEFAULT nextval('"Kali".extended_match_details_request_match_id_seq'::regclass);


--
-- Name: lobby_message lobby_id; Type: DEFAULT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".lobby_message ALTER COLUMN lobby_id SET DEFAULT nextval('"Kali".lobby_message_lobby_id_seq'::regclass);


--
-- Name: match_details match_id; Type: DEFAULT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".match_details ALTER COLUMN match_id SET DEFAULT nextval('"Kali".match_details_match_id_seq'::regclass);


--
-- Name: match_status match_id; Type: DEFAULT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".match_status ALTER COLUMN match_id SET DEFAULT nextval('"Kali".match_status_match_id_seq'::regclass);

--
-- Name: announce_message_lobby_id_seq; Type: SEQUENCE SET; Schema: Kali; Owner: machine_user
--

SELECT pg_catalog.setval('"Kali".announce_message_lobby_id_seq', 1, false);


--
-- Name: balance_ledger_discord_id_seq; Type: SEQUENCE SET; Schema: Kali; Owner: machine_user
--

SELECT pg_catalog.setval('"Kali".balance_ledger_discord_id_seq', 1, false);


--
-- Name: bet_ledger_wager_id_seq; Type: SEQUENCE SET; Schema: Kali; Owner: machine_user
--

SELECT pg_catalog.setval('"Kali".bet_ledger_wager_id_seq', 28391, true);


--
-- Name: charity_charity_id_seq; Type: SEQUENCE SET; Schema: Kali; Owner: machine_user
--

SELECT pg_catalog.setval('"Kali".charity_charity_id_seq', 22416, true);


--
-- Name: extended_match_details_request_match_id_seq; Type: SEQUENCE SET; Schema: Kali; Owner: machine_user
--

SELECT pg_catalog.setval('"Kali".extended_match_details_request_match_id_seq', 1, false);


--
-- Name: lobby_message_lobby_id_seq; Type: SEQUENCE SET; Schema: Kali; Owner: machine_user
--

SELECT pg_catalog.setval('"Kali".lobby_message_lobby_id_seq', 1, false);


--
-- Name: match_details_match_id_seq; Type: SEQUENCE SET; Schema: Kali; Owner: machine_user
--

SELECT pg_catalog.setval('"Kali".match_details_match_id_seq', 1, false);


--
-- Name: match_status_match_id_seq; Type: SEQUENCE SET; Schema: Kali; Owner: machine_user
--

SELECT pg_catalog.setval('"Kali".match_status_match_id_seq', 1, false);


--
-- Name: announce_message announce_message_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".announce_message
    ADD CONSTRAINT announce_message_pkey PRIMARY KEY (lobby_id);


--
-- Name: balance_ledger balance_ledger_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".balance_ledger
    ADD CONSTRAINT balance_ledger_pkey PRIMARY KEY (discord_id);


--
-- Name: bear_details bear_details_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".bear_details
    ADD CONSTRAINT bear_details_pkey PRIMARY KEY (match_id, player_slot);


--
-- Name: bet_ledger bet_ledger_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".bet_ledger
    ADD CONSTRAINT bet_ledger_pkey PRIMARY KEY (wager_id);


--
-- Name: charity charity_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".charity
    ADD CONSTRAINT charity_pkey PRIMARY KEY (charity_id);


--
-- Name: extended_match_details_request extended_match_details_request_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".extended_match_details_request
    ADD CONSTRAINT extended_match_details_request_pkey PRIMARY KEY (match_id);


--
-- Name: lobby_message lobby_message_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".lobby_message
    ADD CONSTRAINT lobby_message_pkey PRIMARY KEY (lobby_id);


--
-- Name: match_details match_details_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".match_details
    ADD CONSTRAINT match_details_pkey PRIMARY KEY (match_id);


--
-- Name: match_status match_status_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".match_status
    ADD CONSTRAINT match_status_pkey PRIMARY KEY (match_id);


--
-- Name: pick_details pick_details_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".pick_details
    ADD CONSTRAINT pick_details_pkey PRIMARY KEY (match_id, "order");


--
-- Name: player_match_details player_match_details_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".player_match_details
    ADD CONSTRAINT player_match_details_pkey PRIMARY KEY (match_id, player_slot);


--
-- Name: rp_heroes rp_heroes_pkey; Type: CONSTRAINT; Schema: Kali; Owner: machine_user
--

ALTER TABLE ONLY "Kali".rp_heroes
    ADD CONSTRAINT rp_heroes_pkey PRIMARY KEY (lobby_id, steam_id, param0, param1);


--
-- PostgreSQL database dump complete
--

--
-- Migrations
--

ALTER TABLE "Kali".discord_ids 
    ADD COLUMN discord_name VARCHAR;